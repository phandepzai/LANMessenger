using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.IO;


namespace Messenger
{
    // Lớp chứa thông tin về sự kiện thay đổi trạng thái gõ phím.
    public class TypingStatusEventArgs : EventArgs
    {
        // Tên của người gửi trạng thái gõ phím.
        public string SenderName { get; }
        // Trạng thái gõ phím (true nếu đang gõ, false nếu dừng gõ).
        public bool IsTyping { get; }
        // Constructor khởi tạo đối tượng TypingStatusEventArgs.
        public TypingStatusEventArgs(string senderName, bool isTyping)
        {
            SenderName = senderName;
            IsTyping = isTyping;
        }
    }

    // Lớp tĩnh chứa các phương thức mở rộng liên quan đến mạng.
    public static class NetworkExtensions
    {
        // Phương thức mở rộng cho Task<T> để thêm khả năng hủy bỏ bằng CancellationToken.
        public static async Task<T> WithCancellation<T>(this Task<T> task, CancellationToken cancellationToken)
        {
            // Tạo một TaskCompletionSource để quản lý trạng thái hủy bỏ.
            var tcs = new TaskCompletionSource<T>();
            // Đăng ký một hành động sẽ được thực hiện khi CancellationToken bị hủy.
            using (cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken)))
            {
                // Chờ cho task gốc hoặc task hủy bỏ hoàn thành.
                var completedTask = await Task.WhenAny(task, tcs.Task).ConfigureAwait(false);
                // Nếu task hoàn thành là task hủy bỏ, ném ra ngoại lệ OperationCanceledException.
                if (completedTask == tcs.Task)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }
                // Nếu không bị hủy, trả về kết quả của task gốc.
                return await task.ConfigureAwait(false);
            }
        }

        // Phương thức mở rộng cho TcpListener để chấp nhận TcpClient một cách bất đồng bộ với hỗ trợ CancellationToken.
        public static async Task<TcpClient> AcceptTcpClientAsync(this TcpListener listener, CancellationToken token)
        {
            // Kiểm tra nếu CancellationToken đã bị hủy, ném ngoại lệ nếu có.
            if (token.IsCancellationRequested) token.ThrowIfCancellationRequested();
            try
            {
                // Bắt đầu tác vụ chấp nhận TcpClient.
                var acceptTask = listener.AcceptTcpClientAsync();
                // Tạo một TaskCompletionSource để theo dõi trạng thái hủy bỏ.
                var tcs = new TaskCompletionSource<bool>();
                // Đăng ký một hành động sẽ đặt kết quả cho TaskCompletionSource khi CancellationToken bị hủy.
                using (token.Register(s => ((TaskCompletionSource<bool>)s).TrySetResult(true), tcs))
                {
                    // Chờ cho tác vụ chấp nhận hoặc tác vụ hủy bỏ hoàn thành.
                    if (acceptTask == await Task.WhenAny(acceptTask, tcs.Task).ConfigureAwait(false))
                    {
                        // Nếu tác vụ chấp nhận hoàn thành trước, trả về TcpClient đã được chấp nhận.
                        return await acceptTask.ConfigureAwait(false);
                    }
                    else
                    {
                        // Nếu tác vụ hủy bỏ hoàn thành trước, ném ngoại lệ OperationCanceledException.
                        token.ThrowIfCancellationRequested();
                        return null;
                    }
                }
            }
            // Bắt ngoại lệ ObjectDisposedException xảy ra khi TcpListener bị giải phóng trong khi CancellationToken bị hủy.
            catch (ObjectDisposedException) when (token.IsCancellationRequested)
            {
                token.ThrowIfCancellationRequested();
                return null;
            }
        }
    }

    // Lớp chứa thông tin về sự kiện tin nhắn đã nhận.
    public class MessageReceivedEventArgs : EventArgs
    {
        // Tin nhắn chat đã nhận.
        public ChatMessage Message { get; }
        // Constructor khởi tạo đối tượng MessageReceivedEventArgs.
        public MessageReceivedEventArgs(ChatMessage message)
        {
            Message = message;
        }
    }

    // Lớp chứa thông tin về sự kiện phát hiện peer mới.
    public class PeerDiscoveryEventArgs : EventArgs
    {
        // Tên của peer được phát hiện.
        public string PeerName { get; }
        // IPEndPoint của peer được phát hiện.
        public IPEndPoint PeerEndPoint { get; }
        // Constructor khởi tạo đối tượng PeerDiscoveryEventArgs.
        public PeerDiscoveryEventArgs(string peerName, IPEndPoint peerEndPoint)
        {
            PeerName = peerName;
            PeerEndPoint = peerEndPoint;
        }
    }

    // Lớp cung cấp các dịch vụ mạng cho ứng dụng chat.
    public class NetworkService : IDisposable
    {
        private UdpClient _udpClient; // Removed readonly to allow re-initialization if needed, though Dispose should prevent it.
        private TcpListener _tcpListener;
        private string _localUserName;
        private int _tcpPort;
        private readonly IPAddress _multicastAddress;
        private readonly int _multicastPort;
        private CancellationTokenSource _cancellationTokenSource;
        private Task _udpListenTask;
        private Task _tcpListenTask;
        private Task _heartbeatTask;
        private Task _peerCleanupTask;

        private ConcurrentDictionary<string, PeerInfo> _activePeers;

        // Sự kiện được gọi khi nhận được một tin nhắn mới.
        public event EventHandler<MessageReceivedEventArgs> MessageReceived;
        // Sự kiện được gọi khi phát hiện một peer mới.
        public event EventHandler<PeerDiscoveryEventArgs> PeerDiscovered;
        // Sự kiện được gọi khi một peer bị ngắt kết nối.
        public event EventHandler<PeerDiscoveryEventArgs> PeerDisconnected;
        // Sự kiện được gọi khi nhận được trạng thái gõ phím từ một peer.
        public event EventHandler<TypingStatusEventArgs> TypingStatusReceived;
        // Danh sách các ID tin nhắn broadcast đã được xử lý để tránh xử lý trùng lặp.
        private readonly HashSet<Guid> _processedBroadcastMessageIds = new HashSet<Guid>();
        // Tên người dùng cục bộ trước khi đổi tên (sử dụng cho việc bỏ qua heartbeat).
        private string _previousLocalUserNameForHeartbeatIgnore = null;
        // Thời điểm đổi tên cuối cùng (sử dụng cho việc bỏ qua heartbeat).
        private DateTime _lastRenameTimeForHeartbeatIgnore = DateTime.MinValue;
        // Thời gian (giây) để bỏ qua heartbeat từ tên cũ sau khi đổi tên.
        private const int HeartbeatIgnoreDurationSeconds = 15;
        // Thuộc tính chỉ đọc để lấy cổng TCP thực tế mà server đang lắng nghe.
        public int ActualTcpPort => _tcpPort;

        // Constructor khởi tạo NetworkService.
        public NetworkService(string userName, int tcpPort, string multicastAddress, int multicastPort)
        {
            _localUserName = userName;
            _multicastAddress = IPAddress.Parse(multicastAddress);
            _multicastPort = multicastPort;
            _cancellationTokenSource = new CancellationTokenSource();
            _activePeers = new ConcurrentDictionary<string, PeerInfo>();

            // Khởi tạo UdpClient và cấu hình để sử dụng lại địa chỉ và lắng nghe trên cổng multicast.
            try
            {
                _udpClient = new UdpClient(); // Initialize UdpClient
                _udpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                _udpClient.Client.Bind(new IPEndPoint(IPAddress.Any, _multicastPort));
                _udpClient.JoinMulticastGroup(_multicastAddress);
                Debug.WriteLine($"[NetworkService] UdpClient đã khởi tạo và tham gia nhóm multicast: {_multicastAddress}:{_multicastPort}");
            }
            catch (SocketException ex)
            {
                Debug.WriteLine($"[NetworkService] Lỗi khi khởi tạo UdpClient hoặc tham gia multicast group: {ex.Message}");
                // Nếu UdpClient không thể khởi tạo, đặt nó về null để tránh sử dụng đối tượng lỗi.
                _udpClient?.Close();
                _udpClient = null;
                throw new InvalidOperationException("Không thể khởi tạo dịch vụ UDP. Vui lòng kiểm tra cấu hình mạng hoặc quyền.", ex);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[NetworkService] Lỗi không xác định khi khởi tạo UdpClient: {ex.Message}");
                _udpClient?.Close();
                _udpClient = null;
                throw;
            }

            // Khởi tạo và bắt đầu TcpListener.
            try
            {
                _tcpListener = new TcpListener(IPAddress.Any, tcpPort);
                _tcpListener.Start();
                _tcpPort = ((IPEndPoint)_tcpListener.LocalEndpoint).Port;
                Debug.WriteLine($"[NetworkService] TCP Listener đã khởi động trên cổng: {_tcpPort}");
            }
            // Xử lý trường hợp cổng TCP đã được sử dụng.
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.AddressAlreadyInUse)
            {
                Debug.WriteLine($"[NetworkService] Cổng TCP {tcpPort} đang được sử dụng, chọn cổng ngẫu nhiên");
                _tcpListener = new TcpListener(IPAddress.Any, 0); // Try with port 0 for dynamic assignment
                _tcpListener.Start();
                _tcpPort = ((IPEndPoint)_tcpListener.LocalEndpoint).Port;
                Debug.WriteLine($"[NetworkService] Đang sử dụng cổng TCP ngẫu nhiên: {_tcpPort}");
            }
            // Xử lý các lỗi khác khi khởi động TCP Listener.
            catch (Exception ex)
            {
                Debug.WriteLine($"[NetworkService] Lỗi khi khởi động TCP Listener: {ex.Message}");
                _tcpListener?.Stop(); // Ensure listener is stopped if it failed to start
                _tcpListener = null;
                throw new InvalidOperationException("Không thể khởi động dịch vụ TCP. Vui lòng kiểm tra cấu hình mạng hoặc quyền.", ex);
            }
        }

        /// <summary>
        /// Ensures the UdpClient is initialized and ready for use.
        /// If it's null, not bound, or disposed, it attempts to re-initialize it.
        /// </summary>
        private void EnsureUdpClientInitialized()
        {
            // Kiểm tra xem UdpClient có đang ở trạng thái hợp lệ hay không.
            // Nếu không, cố gắng khởi tạo lại.
            if (_udpClient == null || _udpClient.Client == null || !_udpClient.Client.IsBound)
            {
                Debug.WriteLine("[NetworkService] UdpClient không khả dụng. Đang cố gắng khởi tạo lại.");
                try
                {
                    // Đóng và giải phóng UdpClient hiện có (nếu có) trước khi khởi tạo lại.
                    _udpClient?.Close();
                    _udpClient?.Dispose();
                    _udpClient = new UdpClient(); // Khởi tạo một UdpClient mới.
                    _udpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true); // Cho phép sử dụng lại địa chỉ.
                    _udpClient.Client.Bind(new IPEndPoint(IPAddress.Any, _multicastPort)); // Bind đến cổng multicast.
                    _udpClient.JoinMulticastGroup(_multicastAddress); // Tham gia nhóm multicast.
                    Debug.WriteLine($"[NetworkService] UdpClient đã khởi tạo lại và tham gia nhóm multicast: {_multicastAddress}:{_multicastPort}");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[NetworkService] Lỗi khi khởi tạo lại UdpClient: {ex.Message}");
                    _udpClient?.Close(); // Đảm bảo đóng nếu có lỗi.
                    _udpClient = null; // Đặt về null nếu khởi tạo lại thất bại.
                    // Ném lại ngoại lệ để thông báo lỗi cho luồng gọi.
                    throw new InvalidOperationException("Không thể khởi tạo lại dịch vụ UDP.", ex);
                }
            }
        }

        // Cập nhật tên người dùng cục bộ.
        public void UpdateLocalUserName(string newUserName)
        {
            if (string.IsNullOrWhiteSpace(newUserName))
            {
                throw new ArgumentException("Tên người dùng mới không được để trống.");
            }

            if (_activePeers.ContainsKey(newUserName) && newUserName != _localUserName)
            {
                throw new InvalidOperationException($"Tên người dùng '{newUserName}' đã được sử dụng bởi một peer khác.");
            }

            string oldUserName = _localUserName;
            _previousLocalUserNameForHeartbeatIgnore = oldUserName;
            _lastRenameTimeForHeartbeatIgnore = DateTime.UtcNow;

            // Cập nhật tên người dùng cục bộ
            _localUserName = newUserName;

            // Cập nhật _activePeers
            PeerInfo localPeerEntry;
            if (_activePeers.TryRemove(oldUserName, out localPeerEntry))
            {
                if (localPeerEntry.IsLocal)
                {
                    var newLocalPeerEntry = new PeerInfo(newUserName, localPeerEntry.EndPoint, true)
                    {
                        LastHeartbeat = localPeerEntry.LastHeartbeat
                    };
                    _activePeers.TryAdd(newUserName, newLocalPeerEntry);
                    Debug.WriteLine($"[NetworkService] Tên người dùng cục bộ đã được cập nhật trong _activePeers: {oldUserName} -> {newUserName}");
                }
                else
                {
                    _activePeers.TryAdd(oldUserName, localPeerEntry);
                    Debug.WriteLine($"[NetworkService] Cảnh báo: Đã cố gắng cập nhật người dùng cục bộ, nhưng mục nhập cho '{oldUserName}' không được đánh dấu là cục bộ. Đã thêm lại.");
                }
            }
            else
            {
                Debug.WriteLine($"[NetworkService] Thông tin: Mục nhập người dùng cục bộ cho '{oldUserName}' không tìm thấy trong _activePeers trong quá trình đổi tên thành '{newUserName}'. Đang thêm mục nhập mới.");
                if (!_activePeers.ContainsKey(newUserName) && _tcpListener != null && _tcpListener.Server.IsBound)
                {
                    var newEp = _tcpListener.LocalEndpoint as IPEndPoint ?? new IPEndPoint(IPAddress.Loopback, _tcpPort);
                    _activePeers.TryAdd(newUserName, new PeerInfo(newUserName, newEp, true));
                }
            }

            // Gửi thông báo cập nhật tên đến các peer khác
            // Sử dụng Task.Run và await để tránh block luồng gọi
            Task.Run(async () => await SendNameUpdate(oldUserName, newUserName)).ConfigureAwait(false);

            // Xóa các tin nhắn broadcast cũ từ tên cũ
            lock (_processedBroadcastMessageIds)
            {
                _processedBroadcastMessageIds.Clear();
            }
        }

        public async Task SendNameUpdate(string oldName, string newName)
        {
            try
            {
                EnsureUdpClientInitialized(); // Ensure UDP client is ready
                string message = $"NAME_UPDATE:{oldName}:{newName}";
                byte[] data = Encoding.UTF8.GetBytes(message);
                if (_udpClient != null)
                {
                    await _udpClient.SendAsync(data, data.Length, new IPEndPoint(_multicastAddress, _multicastPort)).ConfigureAwait(false);
                    Debug.WriteLine($"[NetworkService] Đã gửi NAME_UPDATE: {oldName} -> {newName}");
                }
                else
                {
                    Debug.WriteLine("[NetworkService] Không thể gửi NAME_UPDATE: UdpClient không khả dụng sau khi khởi tạo lại.");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[NetworkService] Lỗi gửi NAME_UPDATE: {ex.Message}");
                throw; // Re-throw to propagate the exception
            }
        }

        public void Start()
        {
            // Ensure cancellation token source is not disposed from previous runs
            if (_cancellationTokenSource.IsCancellationRequested)
            {
                _cancellationTokenSource = new CancellationTokenSource();
            }
            var cancellationToken = _cancellationTokenSource.Token;

            // Re-check TCP listener state and start if necessary
            if (_tcpListener == null || !_tcpListener.Server.IsBound)
            {
                try
                {
                    if (_tcpListener == null) _tcpListener = new TcpListener(IPAddress.Any, _tcpPort);
                    _tcpListener.Start();
                    _tcpPort = ((IPEndPoint)_tcpListener.LocalEndpoint).Port;
                    Debug.WriteLine($"[NetworkService] TCP Listener đã khởi động lại trên cổng: {_tcpPort}");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[NetworkService] Không thể khởi động TCP listener trong Start(): {ex.Message}");
                    throw new InvalidOperationException("Dịch vụ mạng không thể khởi động TCP listener.", ex);
                }
            }

            // Ensure UDP client is initialized at startup
            try
            {
                EnsureUdpClientInitialized();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[NetworkService] Lỗi khi khởi tạo UdpClient trong Start(): {ex.Message}");
                // Không ném ngoại lệ ở đây vì nó có thể đã được ném trong constructor.
                // Nếu UdpClient không khởi tạo được, các tác vụ liên quan đến UDP sẽ tự xử lý.
            }

            // Start tasks and store them
            _udpListenTask = ListenForUdpMessages(cancellationToken);
            _tcpListenTask = ListenForTcpConnections(cancellationToken);
            _heartbeatTask = SendHeartbeatPeriodically(cancellationToken);
            _peerCleanupTask = CleanupDisconnectedPeers(cancellationToken);

            var localEp = _tcpListener.LocalEndpoint as IPEndPoint;
            if (localEp != null)
            {
                _activePeers.AddOrUpdate(_localUserName,
                    new PeerInfo(_localUserName, localEp, true),
                    (key, existingInfo) =>
                    {
                        existingInfo.EndPoint = localEp;
                        existingInfo.LastHeartbeat = DateTime.UtcNow;
                        return existingInfo;
                    });
                Debug.WriteLine($"[NetworkService] Đã thêm/cập nhật người dùng cục bộ '{_localUserName}' vào active peers.");
            }
            else
            {
                Debug.WriteLine("[NetworkService] Cảnh báo: Không thể lấy IPEndPoint cục bộ để thêm vào active peers. Sử dụng Loopback.");
                _activePeers.AddOrUpdate(_localUserName,
                    new PeerInfo(_localUserName, new IPEndPoint(IPAddress.Loopback, _tcpPort), true),
                    (key, existingInfo) =>
                    {
                        existingInfo.EndPoint = new IPEndPoint(IPAddress.Loopback, _tcpPort);
                        existingInfo.LastHeartbeat = DateTime.UtcNow;
                        return existingInfo;
                    });
            }
        }

        public async Task SendMessageToPeer(string peerName, string messageContent)
        {
            // Kiểm tra xem peer có tồn tại trong danh sách các peer đang hoạt động hay không
            if (_activePeers.TryGetValue(peerName, out PeerInfo peerInfo))
            {
                // Nếu cố gắng gửi tin nhắn cho chính mình, bỏ qua
                if (peerInfo.IsLocal && peerInfo.UserName == _localUserName)
                {
                    Debug.WriteLine($"[NetworkService] Đã cố gắng gửi tin nhắn cho chính mình ({peerName}). Hủy bỏ.");
                    return;
                }

                Debug.WriteLine($"[SendMessageToPeer] Đang chuẩn bị gửi đến {peerName} tại {peerInfo.EndPoint}");
                // Kiểm tra xem đã có kết nối TCP đến peer này chưa
                if (peerInfo.TcpClient == null || !peerInfo.TcpClient.Connected)
                {
                    Debug.WriteLine($"[SendMessageToPeer] Không có hoạt động kết nối tới {peerName}. Đang cố gắng kết nối...");
                    // Cố gắng thiết lập kết nối đến peer
                    await ConnectToPeer(peerInfo).ConfigureAwait(false);
                }

                // Nếu đã có kết nối TCP hợp lệ
                if (peerInfo.TcpClient != null && peerInfo.TcpClient.Connected)
                {
                    try
                    {
                        // Lấy stream để gửi dữ liệu qua kết nối TCP
                        var stream = peerInfo.TcpClient.GetStream();
                        // Tạo ID duy nhất cho tin nhắn
                        Guid messageId = Guid.NewGuid();
                        // Tạo nội dung tin nhắn theo định dạng: CHAT:{Tên người gửi}:{MessageId}:{Nội dung tin nhắn}
                        string messageToSend = $"CHAT:{_localUserName}:{messageId}:{messageContent}";
                        // Chuyển nội dung tin nhắn thành mảng byte theo encoding UTF8
                        byte[] contentBytes = Encoding.UTF8.GetBytes(messageToSend);
                        // Lấy độ dài của nội dung tin nhắn và chuyển thành mảng byte (4 byte đầu tiên biểu thị độ dài)
                        byte[] lengthBytes = BitConverter.GetBytes(contentBytes.Length);

                        Debug.WriteLine($"[SendMessageToPeer] Gửi đến {peerName}: {messageToSend}");
                        // Gửi độ dài của tin nhắn trước
                        await stream.WriteAsync(lengthBytes, 0, lengthBytes.Length, _cancellationTokenSource.Token).ConfigureAwait(false);
                        // Sau đó gửi nội dung tin nhắn
                        await stream.WriteAsync(contentBytes, 0, contentBytes.Length, _cancellationTokenSource.Token).ConfigureAwait(false);
                        Debug.WriteLine($"[SendMessageToPeer] Đã gửi thành công đến {peerName}");
                    }
                    catch (SocketException se)
                    {
                        // Xử lý lỗi socket trong quá trình gửi
                        Debug.WriteLine($"[SendMessageToPeer] Socket lỗi khi gửi đến {peerName}: {se.Message}, Mã lỗi: {se.SocketErrorCode}");
                        // Đánh dấu peer là đã ngắt kết nối
                        MarkPeerDisconnected(peerInfo.UserName);
                        throw new InvalidOperationException($"Không thể gửi tin nhắn đến {peerName}: {se.Message}", se);
                    }
                    catch (OperationCanceledException)
                    {
                        Debug.WriteLine($"[SendMessageToPeer] Gửi tin nhắn đến {peerName} đã bị hủy.");
                        MarkPeerDisconnected(peerInfo.UserName);
                        throw;
                    }
                    catch (Exception ex)
                    {
                        // Xử lý các lỗi khác trong quá trình gửi
                        Debug.WriteLine($"[SendMessageToPeer] Lỗi khi gửi đến {peerName}: {ex.Message}");
                        // Đánh dấu peer là đã ngắt kết nối
                        MarkPeerDisconnected(peerInfo.UserName);
                        throw new InvalidOperationException($"Không thể gửi tin nhắn đến {peerName}: {ex.Message}", ex);
                    }
                }
                else
                {
                    // Nếu không thể kết nối đến peer
                    Debug.WriteLine($"[SendMessageToPeer] Không thể kết nối tới {peerName} tại {peerInfo.EndPoint}");
                    throw new InvalidOperationException($"Không thể kết nối đến {peerName}. Vui lòng kiểm tra trạng thái mạng hoặc người dùng.");
                }
            }
            else
            {
                // Nếu peer không tồn tại trong danh sách active peers
                Debug.WriteLine($"[SendMessageToPeer] Người dùng '{peerName}' không tìm thấy trong các user đang hoạt động. Các user có sẵn: {string.Join(", ", _activePeers.Keys)}");
                throw new InvalidOperationException($"Người dùng '{peerName}' không tìm thấy hoặc offline.");
            }
        }

        public async Task SendMulticastMessage(string messageContent)
        {
            try
            {
                EnsureUdpClientInitialized(); // Ensure UDP client is ready
                if (_udpClient == null)
                {
                    Debug.WriteLine("[SendMulticastMessage] Không thể gửi: UdpClient đã bị giải phóng hoặc chưa khởi tạo sau khi khởi tạo lại.");
                    throw new InvalidOperationException("Không thể gửi tin nhắn Broadcast: Dịch vụ UDP không khả dụng.");
                }

                // Tạo nội dung tin nhắn broadcast theo định dạng: BROADCAST:{Tên người gửi}:{MessageId}:{Nội dung tin nhắn}
                string message = $"BROADCAST:{_localUserName}:{Guid.NewGuid()}:{messageContent}";
                // Chuyển nội dung tin nhắn thành mảng byte theo encoding UTF8
                byte[] data = Encoding.UTF8.GetBytes(message);
                Debug.WriteLine($"[SendMulticastMessage] Gửi: {message} đến {_multicastAddress}:{_multicastPort}");
                // Gửi tin nhắn đến địa chỉ multicast và port
                await _udpClient.SendAsync(data, data.Length, new IPEndPoint(_multicastAddress, _multicastPort)).ConfigureAwait(false);
                Debug.WriteLine($"[SendMulticastMessage] Đã gửi thành công đến {_multicastAddress}:{_multicastPort}");
            }
            catch (ObjectDisposedException ex)
            {
                Debug.WriteLine($"[SendMulticastMessage] Lỗi ObjectDisposedException khi gửi broadcast: {ex.Message}");
                throw new InvalidOperationException($"Không thể gửi tin nhắn Broadcast: Dịch vụ UDP đã bị giải phóng. Lỗi: {ex.Message}", ex);
            }
            catch (SocketException se)
            {
                // Xử lý lỗi socket trong quá trình gửi broadcast
                Debug.WriteLine($"[SendMulticastMessage] Socket lỗi: {se.Message}, Mã lỗi: {se.SocketErrorCode}");
                throw new InvalidOperationException($"Không thể gửi tin nhắn Broadcast: {se.Message}", se);
            }
            catch (Exception ex)
            {
                // Xử lý các lỗi khác trong quá trình gửi broadcast
                Debug.WriteLine($"[SendMulticastMessage] Lỗi gửi tin nhắn multicast: {ex.Message}");
                throw new InvalidOperationException($"Không thể gửi tin nhắn Broadcast: {ex.Message}", ex);
            }
        }

        public async Task SendTypingStatus(string senderName, bool isTyping, bool isBroadcast, string targetPeer = null)
        {
            // Xác định loại trạng thái gõ: TYPING_START hoặc TYPING_STOP
            string messageType = isTyping ? "TYPING_START" : "TYPING_STOP";
            // Tạo nội dung tin nhắn trạng thái gõ theo định dạng: {TYPING_STATUS}:{Tên người gửi}
            string messageContent = $"{messageType}:{senderName}";

            // Nếu gửi trạng thái gõ broadcast (cho tất cả mọi người)
            if (isBroadcast)
            {
                try
                {
                    EnsureUdpClientInitialized(); // Ensure UDP client is ready
                    if (_udpClient == null)
                    {
                        Debug.WriteLine("[SendTypingStatus] Không thể gửi broadcast typing status: UdpClient không khả dụng sau khi khởi tạo lại.");
                        return;
                    }
                    // Chuyển nội dung tin nhắn thành mảng byte theo encoding UTF8
                    byte[] data = Encoding.UTF8.GetBytes(messageContent);
                    // Gửi tin nhắn đến địa chỉ multicast và port
                    await _udpClient.SendAsync(data, data.Length, new IPEndPoint(_multicastAddress, _multicastPort)).ConfigureAwait(false);
                }
                catch (ObjectDisposedException ex)
                {
                    Debug.WriteLine($"[SendTypingStatus] Lỗi ObjectDisposedException khi gửi broadcast typing status: {ex.Message}");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Lỗi gửi trạng thái đang nhập broadcast: {ex.Message}");
                }
            }
            // Nếu gửi trạng thái gõ đến một peer cụ thể
            else if (!string.IsNullOrEmpty(targetPeer) && _activePeers.TryGetValue(targetPeer, out PeerInfo peerInfo))
            {
                // Không gửi trạng thái gõ cho chính mình
                if (peerInfo.IsLocal && peerInfo.UserName == _localUserName) return;

                // Kiểm tra và kết nối đến peer nếu chưa có kết nối
                if (peerInfo.TcpClient == null || !peerInfo.TcpClient.Connected)
                {
                    await ConnectToPeer(peerInfo).ConfigureAwait(false);
                }

                // Nếu đã có kết nối TCP hợp lệ
                if (peerInfo.TcpClient != null && peerInfo.TcpClient.Connected)
                {
                    try
                    {
                        // Lấy stream để gửi dữ liệu
                        var stream = peerInfo.TcpClient.GetStream();
                        // Chuyển nội dung tin nhắn thành mảng byte
                        byte[] contentBytes = Encoding.UTF8.GetBytes(messageContent);
                        // Lấy độ dài của nội dung tin nhắn
                        byte[] lengthBytes = BitConverter.GetBytes(contentBytes.Length);

                        // Gửi độ dài và nội dung tin nhắn
                        await stream.WriteAsync(lengthBytes, 0, lengthBytes.Length, _cancellationTokenSource.Token).ConfigureAwait(false);
                        await stream.WriteAsync(contentBytes, 0, contentBytes.Length, _cancellationTokenSource.Token).ConfigureAwait(false);
                    }
                    catch (ObjectDisposedException ex)
                    {
                        Debug.WriteLine($"[SendTypingStatus] Lỗi ObjectDisposedException khi gửi typing status đến {targetPeer}: {ex.Message}");
                        MarkPeerDisconnected(peerInfo.UserName);
                    }
                    catch (OperationCanceledException)
                    {
                        Debug.WriteLine($"[SendTypingStatus] Gửi typing status đến {targetPeer} đã bị hủy.");
                        MarkPeerDisconnected(peerInfo.UserName);
                        throw;
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Lỗi gửi trạng thái đang nhập đến {targetPeer}: {ex.Message}");
                        MarkPeerDisconnected(peerInfo.UserName);
                    }
                }
            }
        }

        private async Task ListenForUdpMessages(CancellationToken cancellationToken)
        {
            // Lắng nghe các tin nhắn UDP đến cho đến khi có yêu cầu hủy bỏ
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    // Đảm bảo UdpClient được khởi tạo trước khi lắng nghe
                    EnsureUdpClientInitialized();
                    if (_udpClient == null || _udpClient.Client == null || !_udpClient.Client.IsBound)
                    {
                        Debug.WriteLine("[NetworkService] UdpClient không khả dụng hoặc chưa được bind trong ListenForUdpMessages. Đang chờ...");
                        await Task.Delay(1000, cancellationToken).ConfigureAwait(false); // Wait before retrying
                        continue;
                    }
                    // Nhận dữ liệu UDP bất đồng bộ với hỗ trợ hủy bỏ
                    UdpReceiveResult result = await _udpClient.ReceiveAsync().WithCancellation(cancellationToken).ConfigureAwait(false);
                    // Chuyển dữ liệu byte nhận được thành chuỗi UTF8, loại bỏ các ký tự null ở cuối
                    string message = Encoding.UTF8.GetString(result.Buffer, 0, result.Buffer.Length).TrimEnd('\0');
                    // Xử lý tin nhắn UDP nhận được
                    ProcessUdpMessage(message, result.RemoteEndPoint);
                }
                catch (OperationCanceledException)
                {
                    // Xử lý trường hợp thao tác bị hủy bỏ (do CancellationToken)
                    Debug.WriteLine("[NetworkService] ListenForUdpMessages đã bị hủy.");
                    break;
                }
                catch (ObjectDisposedException ex)
                {
                    // Xử lý trường hợp UdpClient đã bị giải phóng
                    Debug.WriteLine($"[NetworkService] Lỗi ObjectDisposedException trong ListenForUdpMessages: {ex.Message}");
                    _udpClient = null; // Đặt về null để buộc khởi tạo lại
                    // Không break ở đây để cho phép vòng lặp thử khởi tạo lại UdpClient
                    await Task.Delay(1000, cancellationToken).ConfigureAwait(false); // Chờ trước khi thử lại
                }
                catch (SocketException se)
                {
                    // Xử lý lỗi socket trong quá trình lắng nghe UDP
                    Debug.WriteLine($"[NetworkService] Lỗi Socket trong ListenForUdpMessages: {se.Message} (Mã lỗi: {se.SocketErrorCode})");
                    // Nếu không có yêu cầu hủy bỏ, đợi một chút trước khi tiếp tục lắng nghe
                    if (!cancellationToken.IsCancellationRequested) await Task.Delay(100, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    // Xử lý các lỗi khác trong quá trình lắng nghe UDP
                    Debug.WriteLine($"[NetworkService] Lỗi trong ListenForUdpMessages: {ex.Message}");
                    // Nếu không có yêu cầu hủy bỏ, đợi một chút trước khi tiếp tục lắng nghe
                    if (!cancellationToken.IsCancellationRequested) await Task.Delay(100, cancellationToken).ConfigureAwait(false);
                }
            }
        }

        private void ProcessUdpMessage(string message, IPEndPoint remoteEndPoint)
        {
            Debug.WriteLine($"[ProcessUdpMessage] Đã nhận: {message} từ {remoteEndPoint}");
            var partsGeneral = message.Split(new[] { ':' }, 3);
            string potentialSenderName = partsGeneral.Length > 1 ? partsGeneral[1] : "Không rõ";

            // Bỏ qua tin nhắn từ chính mình hoặc từ tên cũ trong vòng 15 giây sau khi đổi tên
            if ((potentialSenderName == _localUserName ||
                 (potentialSenderName == _previousLocalUserNameForHeartbeatIgnore &&
                  _previousLocalUserNameForHeartbeatIgnore != _localUserName &&
                  (DateTime.UtcNow - _lastRenameTimeForHeartbeatIgnore).TotalSeconds < HeartbeatIgnoreDurationSeconds)) &&
                !message.StartsWith("HEARTBEAT:"))
            {
                Debug.WriteLine($"[ProcessUdpMessage] Bỏ qua tin nhắn từ chính mình hoặc tên cũ: {message}");
                return;
            }

            if (message.StartsWith("HEARTBEAT:"))
            {
                Debug.WriteLine($"[ProcessUdpMessage] Xử lý HEARTBEAT: {message}");
                var parts = message.Split(':');
                if (parts.Length == 3)
                {
                    string peerName = parts[1];
                    if (int.TryParse(parts[2], out int port))
                    {
                        var peerServiceEndPoint = new IPEndPoint(remoteEndPoint.Address, port);
                        HandlePeerHeartbeat(peerName, peerServiceEndPoint);
                    }
                    else
                    {
                        Debug.WriteLine($"[ProcessUdpMessage] Cổng không hợp lệ trong HEARTBEAT: {parts[2]}");
                    }
                }
                else
                {
                    Debug.WriteLine($"[ProcessUdpMessage] Định dạng HEARTBEAT không hợp lệ: {message}");
                }
            }
            else if (message.StartsWith("BROADCAST:"))
            {
                Debug.WriteLine($"[ProcessUdpMessage] Nhận BROADCAST: {message} từ {remoteEndPoint}");
                var parts = message.Split(new[] { ':' }, 4);
                if (parts.Length == 4)
                {
                    string senderName = parts[1];
                    if (Guid.TryParse(parts[2], out Guid messageId))
                    {
                        string content = parts[3];
                        if (senderName == _localUserName)
                        {
                            Debug.WriteLine($"[ProcessUdpMessage] Bỏ qua BROADCAST từ chính mình: {senderName}");
                            return;
                        }
                        if (!_processedBroadcastMessageIds.Contains(messageId))
                        {
                            Debug.WriteLine($"[ProcessUdpMessage] Xử lý BROADCAST từ {senderName}: {content}");
                            _processedBroadcastMessageIds.Add(messageId);
                            OnMessageReceived(new ChatMessage(senderName, content, false, messageId));
                        }
                        else
                        {
                            Debug.WriteLine($"[ProcessUdpMessage] Bỏ qua BROADCAST trùng lặp (MessageId: {messageId}) từ {senderName}");
                        }
                    }
                    else
                    {
                        Debug.WriteLine($"[ProcessUdpMessage] MessageId không hợp lệ trong BROADCAST: {parts[2]}");
                    }
                }
                else
                {
                    Debug.WriteLine($"[ProcessUdpMessage] Định dạng BROADCAST không hợp lệ: {message}, parts: {parts.Length}");
                }
            }
            else if (message.StartsWith("TYPING_START:") || message.StartsWith("TYPING_STOP:"))
            {
                var parts = message.Split(':');
                if (parts.Length == 2)
                {
                    string senderName = parts[1];
                    bool isTyping = message.StartsWith("TYPING_START:");
                    if (senderName != _localUserName)
                    {
                        TypingStatusReceived?.Invoke(this, new TypingStatusEventArgs(senderName, isTyping));
                    }
                }
            }
            else if (message.StartsWith("NAME_UPDATE:"))
            {
                var parts = message.Split(':');
                if (parts.Length == 3)
                {
                    string oldName = parts[1];
                    string newName = parts[2];
                    HandleNameUpdate(oldName, newName, remoteEndPoint);
                }
            }
        }

        private void HandleNameUpdate(string oldName, string newName, IPEndPoint remoteEndPoint)
        {
            // Cố gắng xóa peer cũ khỏi danh sách active peers
            if (_activePeers.TryRemove(oldName, out PeerInfo peerInfo))
            {
                // Tạo thông tin peer mới với tên mới nhưng giữ nguyên endpoint và trạng thái kết nối (nếu có)
                var newPeerInfo = new PeerInfo(newName, peerInfo.EndPoint, false)
                {
                    LastHeartbeat = peerInfo.LastHeartbeat
                };
                // Chuyển TcpClient (nếu đang kết nối) sang thông tin peer mới
                if (peerInfo.TcpClient != null && peerInfo.TcpClient.Connected)
                {
                    newPeerInfo.SetTcpClient(peerInfo.TcpClient);
                }
                // Thêm thông tin peer mới vào danh sách active peers
                _activePeers.TryAdd(newName, newPeerInfo);
                Debug.WriteLine($"[NetworkService] Đã cập nhật tên người dùng: {oldName} -> {newName}");
                // Thông báo rằng peer cũ đã ngắt kết nối (về mặt tên)
                PeerDisconnected?.Invoke(this, new PeerDiscoveryEventArgs(oldName, peerInfo.EndPoint));
                // Thông báo rằng một peer mới (với tên mới) đã được tìm thấy
                PeerDiscovered?.Invoke(this, new PeerDiscoveryEventArgs(newName, newPeerInfo.EndPoint));
            }
            else
            {
                // Nếu không tìm thấy peer cũ, có thể là một peer mới hoàn toàn
                Debug.WriteLine($"[NetworkService] Người dùng {oldName} không tìm thấy để cập nhật thành {newName}. Đang thêm như người dùng mới.");
                var newPeerInfo = new PeerInfo(newName, remoteEndPoint, false)
                {
                    LastHeartbeat = DateTime.UtcNow
                };
                _activePeers.TryAdd(newName, newPeerInfo);
                PeerDiscovered?.Invoke(this, new PeerDiscoveryEventArgs(newName, newPeerInfo.EndPoint));
            }
        }

        private void HandlePeerHeartbeat(string peerName, IPEndPoint peerServiceEndPoint)
        {
            // Nếu heartbeat đến từ chính mình, bỏ qua
            if (peerName == _localUserName)
            {
                Debug.WriteLine($"[HandlePeerHeartbeat] Không xử lý các tín hiệu kiểm tra mà chính mình tự gửi ra: {peerName}");
                return;
            }
            // Nếu heartbeat đến từ tên cũ của chính mình trong khoảng thời gian ngắn sau khi đổi tên, bỏ qua
            if (peerName == _previousLocalUserNameForHeartbeatIgnore &&
                _previousLocalUserNameForHeartbeatIgnore != _localUserName &&
                (DateTime.UtcNow - _lastRenameTimeForHeartbeatIgnore).TotalSeconds < HeartbeatIgnoreDurationSeconds)
            {
                Debug.WriteLine($"[HandlePeerHeartbeat] Bỏ qua tín hiệu heartbeat đến từ người dùng vừa đổi tên: {peerName}");
                return;
            }

            // Cập nhật hoặc thêm thông tin của peer vào dictionary _activePeers
            _activePeers.AddOrUpdate(
                peerName,
                (key) =>
                {
                    Debug.WriteLine($"[HandlePeerHeartbeat] Phát hiện ra người dùng mới: {peerName} at {peerServiceEndPoint}");
                    return new PeerInfo(peerName, peerServiceEndPoint, false);
                },
                (key, existingPeer) =>
                {
                    existingPeer.LastHeartbeat = DateTime.UtcNow;
                    if (!existingPeer.EndPoint.Equals(peerServiceEndPoint))
                    {
                        Debug.WriteLine($"[HandlePeerHeartbeat] Người dùng {peerName} đã thay đổi lần cuối từ {existingPeer.EndPoint} đến {peerServiceEndPoint}");
                        existingPeer.TcpClient?.Close(); // Close old TCP client if endpoint changed
                        existingPeer.SetTcpClient(null); // Clear TCP client to force re-connection
                        existingPeer.EndPoint = peerServiceEndPoint; // Update endpoint after closing old client
                    }
                    return existingPeer;
                }
            );

            // Gọi sự kiện PeerDiscovered để thông báo về peer mới hoặc peer có endpoint đã được cập nhật
            PeerDiscovered?.Invoke(this, new PeerDiscoveryEventArgs(peerName, peerServiceEndPoint));
            Debug.WriteLine($"[HandlePeerHeartbeat] Đã phát hiện người dùng được kích hoạt cho {peerName} tại {peerServiceEndPoint}");
            Debug.WriteLine($"[HandlePeerHeartbeat] Những người dùng đang hoạt động hiện tại: {string.Join(", ", _activePeers.Keys)}");
        }

        private async Task ListenForTcpConnections(CancellationToken cancellationToken)
        {
            // Kiểm tra nếu TCP listener chưa được khởi động hoặc chưa bind vào một port
            if (_tcpListener == null || !_tcpListener.Server.IsBound)
            {
                Debug.WriteLine("[NetworkService] TCP Listener chưa được khởi động trong ListenForTcpConnections. Đang hủy tác vụ.");
                return;
            }

            // Lặp vô hạn cho đến khi có yêu cầu hủy bỏ
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    // Chấp nhận một kết nối TCP client đến một cách bất đồng bộ
                    TcpClient client = await _tcpListener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
                    // Nếu có một client kết nối thành công
                    if (client != null)
                    {
                        Debug.WriteLine($"[NetworkService] Đã chấp nhận kết nối TCP từ {client.Client.RemoteEndPoint}");
                        // Xử lý client trên một task riêng để không block luồng chính
                        _ = Task.Run(() => HandleIncomingTcpClient(client, cancellationToken), cancellationToken).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException)
                {
                    // Xử lý trường hợp task bị hủy bỏ (do CancellationToken)
                    Debug.WriteLine("[NetworkService] ListenForTcpConnections đã bị hủy.");
                    break;
                }
                catch (ObjectDisposedException ex)
                {
                    Debug.WriteLine($"[NetworkService] Lỗi ObjectDisposedException trong ListenForTcpConnections: {ex.Message}");
                    // Nếu TcpListener bị dispose, chúng ta không thể tiếp tục lắng nghe.
                    // Cần xử lý việc khởi động lại TcpListener nếu muốn tiếp tục.
                    break;
                }
                catch (SocketException se) when (se.SocketErrorCode == SocketError.Interrupted || se.SocketErrorCode == SocketError.OperationAborted)
                {
                    // Xử lý trường hợp socket bị gián đoạn hoặc hủy bỏ (thường xảy ra khi TCP listener bị dừng)
                    Debug.WriteLine("[NetworkService] TCP Listener chấp nhận bị gián đoạn hoặc hủy bỏ, có thể do tắt máy hoặc hủy bỏ.");
                    break;
                }
                catch (Exception ex)
                {
                    // Xử lý các lỗi khác có thể xảy ra trong quá trình chấp nhận kết nối
                    Debug.WriteLine($"[NetworkService] Lỗi trong ListenForTcpConnections: {ex.Message}");
                    if (!cancellationToken.IsCancellationRequested) await Task.Delay(100, cancellationToken).ConfigureAwait(false);
                }
            }
        }

        private async Task HandleIncomingTcpClient(TcpClient client, CancellationToken cancellationToken)
        {
            IPEndPoint remoteEndPoint = null;
            string associatedPeerName = null;
            HashSet<Guid> processedMessageIds = new HashSet<Guid>();

            try
            {
                // Lấy IPEndPoint của client kết nối đến
                remoteEndPoint = client.Client.RemoteEndPoint as IPEndPoint;
                Debug.WriteLine($"[NetworkService] Đã chấp nhận kết nối TCP từ {remoteEndPoint}");

                // Sử dụng các khối using để đảm bảo tài nguyên được giải phóng khi không còn sử dụng
                using (client) // client will be disposed when exiting this block
                using (var stream = client.GetStream())
                {
                    // Lặp lại cho đến khi có yêu cầu hủy bỏ hoặc client ngắt kết nối
                    while (!cancellationToken.IsCancellationRequested && client.Connected)
                    {
                        try
                        {
                            // Kiểm tra xem có dữ liệu đến trên stream không
                            if (stream.DataAvailable)
                            {
                                // Đọc độ dài của tin nhắn (được gửi trước nội dung)
                                byte[] lengthBytes = new byte[4];
                                int bytesRead = await stream.ReadAsync(lengthBytes, 0, 4, cancellationToken).ConfigureAwait(false);
                                if (bytesRead != 4)
                                {
                                    Debug.WriteLine($"[HandleIncomingTcpClient] Không đọc đủ 4 byte độ dài từ {remoteEndPoint}.");
                                    break; // Connection likely closed
                                }
                                int length = BitConverter.ToInt32(lengthBytes, 0);

                                // Đọc nội dung tin nhắn dựa trên độ dài đã đọc
                                byte[] buffer = new byte[length];
                                bytesRead = await stream.ReadAsync(buffer, 0, length, cancellationToken).ConfigureAwait(false);
                                if (bytesRead != length)
                                {
                                    Debug.WriteLine($"[HandleIncomingTcpClient] Không đọc đủ nội dung tin nhắn từ {remoteEndPoint}.");
                                    break; // Connection likely closed
                                }
                                // Chuyển buffer byte thành chuỗi UTF8
                                string message = Encoding.UTF8.GetString(buffer);

                                // Xử lý tin nhắn
                                ProcessTcpMessage(message, remoteEndPoint, client, ref associatedPeerName, processedMessageIds);
                            }
                            else
                            {
                                // Chờ một khoảng thời gian ngắn để không chiếm dụng CPU
                                await Task.Delay(100, cancellationToken).ConfigureAwait(false);
                            }
                        }
                        catch (IOException ex) when (ex.InnerException is SocketException se && se.SocketErrorCode == SocketError.ConnectionReset)
                        {
                            // Xử lý trường hợp client đột ngột ngắt kết nối
                            Debug.WriteLine($"[NetworkService] Client đã ngắt kết nối: {ex.Message}");
                            break;
                        }
                        catch (OperationCanceledException)
                        {
                            // Xử lý trường hợp task bị hủy bỏ
                            Debug.WriteLine($"[NetworkService] HandleIncomingTcpClient đã bị hủy cho {remoteEndPoint}.");
                            break;
                        }
                        catch (ObjectDisposedException ex)
                        {
                            Debug.WriteLine($"[NetworkService] Lỗi ObjectDisposedException trong HandleIncomingTcpClient cho {remoteEndPoint}: {ex.Message}");
                            break;
                        }
                        catch (Exception ex)
                        {
                            // Xử lý các lỗi khác có thể xảy ra trong quá trình đọc dữ liệu
                            Debug.WriteLine($"[NetworkService] Lỗi xử lý client TCP từ {remoteEndPoint}: {ex.Message}");
                            break;
                        }
                    }
                }
            }
            finally
            {
                // Đảm bảo log khi kết nối TCP bị đóng
                Debug.WriteLine($"[NetworkService] Đã đóng kết nối TCP với {remoteEndPoint} (Người dùng: {associatedPeerName})");
                // Mark peer as disconnected if it was associated with a name
                if (associatedPeerName != null)
                {
                    MarkPeerDisconnected(associatedPeerName);
                }
            }
        }

        private string ProcessTcpMessage(string message, IPEndPoint remoteEndPoint, TcpClient client, ref string currentAssociatedPeer, HashSet<Guid> processedMessageIds)
        {
            string identifiedSenderName = currentAssociatedPeer;

            if (message.StartsWith("CHAT:"))
            {
                var parts = message.Split(new[] { ':' }, 4);
                if (parts.Length == 4)
                {
                    string senderName = parts[1];
                    identifiedSenderName = senderName; // Update associated peer name
                    if (Guid.TryParse(parts[2], out Guid messageId))
                    {
                        string content = parts[3];
                        if (senderName == _localUserName)
                        {
                            Debug.WriteLine($"[ProcessTcpMessage] Bỏ qua tin nhắn CHAT từ chính mình: {senderName}");
                            return identifiedSenderName;
                        }
                        if (!processedMessageIds.Contains(messageId))
                        {
                            OnMessageReceived(new ChatMessage(senderName, content, false, messageId));
                            processedMessageIds.Add(messageId);
                            Debug.WriteLine($"[ProcessTcpMessage] Xử lý CHAT từ {senderName}: {content}");
                        }
                        else
                        {
                            Debug.WriteLine($"[ProcessTcpMessage] Bỏ qua CHAT trùng lặp (MessageId: {messageId}) từ {senderName}");
                        }

                        if (_activePeers.TryGetValue(senderName, out PeerInfo peerInfo))
                        {
                            if (peerInfo.TcpClient != client)
                            {
                                peerInfo.SetTcpClient(client);
                                Debug.WriteLine($"[NetworkService] Đã liên kết client TCP từ {remoteEndPoint} với người dùng {senderName}.");
                            }
                        }
                        else
                        {
                            Debug.WriteLine($"[NetworkService] Đã nhận TCP CHAT từ peer không xác định {senderName} tại {remoteEndPoint}. Tin nhắn: {content}");
                            // Consider adding this peer to active peers if it's a new one
                            var newPeerInfo = new PeerInfo(senderName, new IPEndPoint(remoteEndPoint.Address, 0), false);
                            newPeerInfo.SetTcpClient(client);
                            _activePeers.TryAdd(senderName, newPeerInfo);
                            PeerDiscovered?.Invoke(this, new PeerDiscoveryEventArgs(senderName, newPeerInfo.EndPoint));
                        }
                    }
                    else
                    {
                        Debug.WriteLine($"[ProcessTcpMessage] MessageId không hợp lệ trong CHAT: {parts[2]}");
                    }
                }
            }
            else if (message.StartsWith("HELLO:"))
            {
                var parts = message.Split(new[] { ':' }, 2);
                if (parts.Length == 2)
                {
                    string senderName = parts[1];
                    identifiedSenderName = senderName; // Update associated peer name
                    Debug.WriteLine($"[NetworkService] Đã nhận HELLO từ {senderName} qua TCP từ {remoteEndPoint}.");
                    if (_activePeers.TryGetValue(senderName, out PeerInfo peerInfo))
                    {
                        peerInfo.LastHeartbeat = DateTime.UtcNow;
                        if (peerInfo.TcpClient != client)
                        {
                            peerInfo.SetTcpClient(client);
                            Debug.WriteLine($"[NetworkService] Đã liên kết client TCP (qua HELLO) từ {remoteEndPoint} với người dùng {senderName}.");
                        }
                    }
                    else
                    {
                        var newPeerInfo = new PeerInfo(senderName, new IPEndPoint(remoteEndPoint.Address, 0), false);
                        newPeerInfo.SetTcpClient(client);
                        _activePeers.AddOrUpdate(senderName, newPeerInfo, (k, existing) =>
                        {
                            existing.SetTcpClient(client);
                            existing.LastHeartbeat = DateTime.UtcNow;
                            return existing;
                        });
                        PeerDiscovered?.Invoke(this, new PeerDiscoveryEventArgs(senderName, newPeerInfo.EndPoint));
                        Debug.WriteLine($"[NetworkService] Người dùng {senderName} được phát hiện qua TCP HELLO từ {remoteEndPoint}. Đã thêm/cập nhật active peers.");
                    }
                }
            }
            else if (message.StartsWith("TYPING_START:") || message.StartsWith("TYPING_STOP:"))
            {
                var parts = message.Split(':');
                if (parts.Length == 2)
                {
                    string senderName = parts[1];
                    identifiedSenderName = senderName; // Update associated peer name
                    if (senderName != _localUserName)
                    {
                        bool isTyping = message.StartsWith("TYPING_START:");
                        TypingStatusReceived?.Invoke(this, new TypingStatusEventArgs(senderName, isTyping));
                    }
                }
            }
            return identifiedSenderName;
        }

        private async Task ConnectToPeer(PeerInfo peerInfo)
        {
            // Kiểm tra xem peer có phải là cục bộ hay đã kết nối TCP hay không. Nếu đúng, bỏ qua việc kết nối.
            if (peerInfo.IsLocal || (peerInfo.TcpClient != null && peerInfo.TcpClient.Connected))
            {
                // Ghi log thông báo rằng việc kết nối đến peer này đã bị bỏ qua.
                Debug.WriteLine($"[ConnectToPeer] Bỏ qua kết nối tới {peerInfo.UserName}: Local hoặc đã kết nối");
                return;
            }

            // Ghi log thông báo rằng đang cố gắng kết nối đến peer.
            Debug.WriteLine($"[ConnectToPeer] Đang cố gắng kết nối tới {peerInfo.UserName} tại {peerInfo.EndPoint}");
            TcpClient client = null;
            try
            {
                // Tạo một TcpClient mới để kết nối.
                client = new TcpClient();
                // Tắt độ trễ Nagle để gửi dữ liệu ngay lập tức.
                client.NoDelay = true;
                // Bắt đầu tác vụ kết nối không đồng bộ.
                var connectTask = client.ConnectAsync(peerInfo.EndPoint.Address, peerInfo.EndPoint.Port);
                // Tạo một tác vụ delay để giới hạn thời gian kết nối.
                var timeoutTask = Task.Delay(3000, _cancellationTokenSource.Token);

                // Chờ cho một trong hai tác vụ (kết nối hoặc timeout) hoàn thành.
                if (await Task.WhenAny(connectTask, timeoutTask).ConfigureAwait(false) == connectTask && !connectTask.IsFaulted)
                {
                    // Kiểm tra xem client đã kết nối thành công chưa.
                    if (client.Connected)
                    {
                        // Gán TcpClient đã kết nối cho thông tin peer.
                        peerInfo.SetTcpClient(client);
                        // Ghi log thông báo kết nối thành công.
                        Debug.WriteLine($"[ConnectToPeer] Đã kết nối thành công tới {peerInfo.UserName} tại {peerInfo.EndPoint}");
                        // Gửi tin nhắn HELLO qua TCP sau khi kết nối thành công.
                        await SendTcpHelloMessage(peerInfo).ConfigureAwait(false);
                    }
                    else
                    {
                        // Đóng client nếu kết nối hoàn thành nhưng không thành công.
                        client.Close();
                        // Ghi log thông báo kết nối hoàn thành nhưng không ở trạng thái đã kết nối.
                        Debug.WriteLine($"[ConnectToPeer] Kết nối tới {peerInfo.UserName} đã hoàn thành nhưng chưa kết nối");
                        // Đặt TcpClient của peer thành null.
                        peerInfo.SetTcpClient(null);
                    }
                }
                else
                {
                    // Đóng client nếu kết nối bị timeout hoặc gặp lỗi.
                    client.Close();
                    // Kiểm tra xem tác vụ kết nối có bị lỗi hay không.
                    if (connectTask.IsFaulted)
                    {
                        // Ghi log thông báo lỗi kết nối.
                        Debug.WriteLine($"[ConnectToPeer] Kết nối tới {peerInfo.UserName} bị lỗi: {connectTask.Exception?.InnerException?.Message}");
                    }
                    else
                    {
                        // Ghi log thông báo kết nối bị timeout.
                        Debug.WriteLine($"[ConnectToPeer] Kết nối tới {peerInfo.UserName} đã hết thời gian chờ sau 3 giây");
                    }
                    // Đặt TcpClient của peer thành null.
                    peerInfo.SetTcpClient(null);
                    // Ném một ngoại lệ InvalidOperationException để thông báo lỗi kết nối.
                    throw new InvalidOperationException($"Không thể kết nối đến {peerInfo.UserName}: Hết thời gian hoặc lỗi.");
                }
            }
            // Bắt lỗi SocketException có thể xảy ra trong quá trình kết nối.
            catch (SocketException se)
            {
                // Đóng client nếu có lỗi socket.
                client?.Close();
                // Ghi log thông báo lỗi socket.
                Debug.WriteLine($"[ConnectToPeer] Kết nối lỗi cho {peerInfo.UserName} tại {peerInfo.EndPoint}: {se.Message}, Mã lỗi: {se.SocketErrorCode}");
                // Đặt TcpClient của peer thành null.
                peerInfo.SetTcpClient(null);
                // Ném một ngoại lệ InvalidOperationException kèm theo thông tin lỗi socket.
                throw new InvalidOperationException($"Lỗi mạng khi kết nối đến {peerInfo.UserName}: {se.Message}", se);
            }
            // Bắt lỗi OperationCanceledException nếu tác vụ bị hủy.
            catch (OperationCanceledException)
            {
                // Đóng client nếu tác vụ bị hủy.
                client?.Close();
                // Ghi log thông báo kết nối bị hủy.
                Debug.WriteLine($"[ConnectToPeer] Kết nối tới {peerInfo.UserName} đã bị hủy");
                // Đặt TcpClient của peer thành null.
                peerInfo.SetTcpClient(null);
                // Ném lại ngoại lệ OperationCanceledException.
                throw;
            }
            // Bắt bất kỳ ngoại lệ nào khác xảy ra trong quá trình kết nối.
            catch (Exception ex)
            {
                // Đóng client nếu có lỗi không xác định.
                client?.Close();
                // Ghi log thông báo lỗi không mong muốn.
                Debug.WriteLine($"[ConnectToPeer] Lỗi không mong muốn khi kết nối tới {peerInfo.UserName}: {ex.Message}");
                // Đặt TcpClient của peer thành null.
                peerInfo.SetTcpClient(null);
                // Ném một ngoại lệ InvalidOperationException kèm theo thông tin lỗi.
                throw new InvalidOperationException($"Lỗi không xác định khi kết nối đến {peerInfo.UserName}: {ex.Message}", ex);
            }
        }

        private async Task SendTcpHelloMessage(PeerInfo peerInfo)
        {
            // Kiểm tra xem TcpClient của peer có tồn tại và đang kết nối hay không.
            if (peerInfo.TcpClient != null && peerInfo.TcpClient.Connected)
            {
                try
                {
                    // Lấy luồng (stream) để gửi dữ liệu qua kết nối TCP.
                    var stream = peerInfo.TcpClient.GetStream();
                    // Tạo tin nhắn HELLO chứa tên người dùng cục bộ.
                    string message = $"HELLO:{_localUserName}";
                    // Chuyển đổi tin nhắn thành mảng byte sử dụng mã hóa UTF8.
                    byte[] contentBytes = Encoding.UTF8.GetBytes(message);
                    // Lấy độ dài của nội dung tin nhắn và chuyển đổi thành mảng byte.
                    byte[] lengthBytes = BitConverter.GetBytes(contentBytes.Length);

                    // Gửi độ dài của tin nhắn trước.
                    await stream.WriteAsync(lengthBytes, 0, lengthBytes.Length, _cancellationTokenSource.Token).ConfigureAwait(false);
                    // Gửi nội dung tin nhắn.
                    await stream.WriteAsync(contentBytes, 0, contentBytes.Length, _cancellationTokenSource.Token).ConfigureAwait(false);
                    // Ghi log thông báo đã gửi tin nhắn HELLO.
                    Debug.WriteLine($"[NetworkService] Đã gửi HELLO đến {peerInfo.UserName}");
                }
                // Bắt lỗi OperationCanceledException nếu tác vụ bị hủy.
                catch (OperationCanceledException)
                {
                    // Ghi log thông báo việc gửi HELLO đã bị hủy.
                    Debug.WriteLine($"[NetworkService] Việc gửi HELLO đến {peerInfo.UserName} đã bị hủy.");
                    // Đóng TcpClient và đặt lại thành null.
                    peerInfo.TcpClient?.Close(); // Use null-conditional operator
                    peerInfo.SetTcpClient(null);
                }
                // Bắt bất kỳ ngoại lệ nào khác xảy ra trong quá trình gửi.
                catch (Exception ex)
                {
                    // Ghi log thông báo lỗi khi gửi HELLO.
                    Debug.WriteLine($"[NetworkService] Lỗi khi gửi HELLO đến {peerInfo.UserName}: {ex.Message}");
                    // Đóng TcpClient và đặt lại thành null.
                    peerInfo.TcpClient?.Close(); // Use null-conditional operator
                    peerInfo.SetTcpClient(null);
                }
            }
        }

        private async Task SendHeartbeatPeriodically(CancellationToken cancellationToken)
        {
            // Vòng lặp tiếp tục cho đến khi có yêu cầu hủy bỏ.
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    EnsureUdpClientInitialized(); // Ensure UDP client is ready
                    if (_udpClient == null)
                    {
                        Debug.WriteLine("[SendHeartbeatPeriodically] UdpClient không khả dụng sau khi khởi tạo lại. Bỏ qua gửi heartbeat.");
                        await Task.Delay(1000, cancellationToken).ConfigureAwait(false); // Wait before retrying
                        continue;
                    }
                    // Tạo tin nhắn heartbeat chứa tên người dùng cục bộ và cổng TCP đang lắng nghe.
                    string heartbeatMessage = $"HEARTBEAT:{_localUserName}:{_tcpPort}";
                    // Chuyển đổi tin nhắn heartbeat thành mảng byte sử dụng mã hóa UTF8.
                    byte[] data = Encoding.UTF8.GetBytes(heartbeatMessage);
                    // Gửi tin nhắn heartbeat qua UDP đến địa chỉ multicast và cổng multicast.
                    await _udpClient.SendAsync(data, data.Length, new IPEndPoint(_multicastAddress, _multicastPort)).ConfigureAwait(false);
                    // Ghi log thông báo đã gửi heartbeat.
                    Debug.WriteLine($"[SendHeartbeatPeriodically] Đã gửi heartbeat: {heartbeatMessage} đến {_multicastAddress}:{_multicastPort}");
                    // Chờ 10 giây trước khi gửi heartbeat tiếp theo.
                    await Task.Delay(10000, cancellationToken).ConfigureAwait(false);
                }
                // Bắt lỗi OperationCanceledException nếu tác vụ bị hủy.
                catch (OperationCanceledException)
                {
                    // Ghi log thông báo tác vụ đã bị hủy và thoát khỏi vòng lặp.
                    Debug.WriteLine("[SendHeartbeatPeriodically] Tác vụ đã bị hủy");
                    break;
                }
                catch (ObjectDisposedException ex)
                {
                    Debug.WriteLine($"[SendHeartbeatPeriodically] Lỗi ObjectDisposedException: {ex.Message}. UdpClient đã bị giải phóng.");
                    _udpClient = null; // Đặt về null để buộc khởi tạo lại
                    await Task.Delay(1000, cancellationToken).ConfigureAwait(false); // Chờ trước khi thử lại
                }
                // Bắt bất kỳ ngoại lệ nào khác xảy ra trong quá trình gửi heartbeat.
                catch (Exception ex)
                {
                    // Ghi log thông báo lỗi.
                    Debug.WriteLine($"[SendHeartbeatPeriodically] Lỗi: {ex.Message}");
                    // Nếu không có yêu cầu hủy bỏ, chờ 1 giây trước khi tiếp tục vòng lặp.
                    if (!cancellationToken.IsCancellationRequested) await Task.Delay(1000, cancellationToken).ConfigureAwait(false);
                }
            }
        }

        private async Task CleanupDisconnectedPeers(CancellationToken cancellationToken)
        {
            // Vòng lặp tiếp tục cho đến khi có yêu cầu hủy bỏ.
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    // Lấy thời điểm hiện tại theo giờ UTC.
                    var now = DateTime.UtcNow;
                    // Tạo một danh sách để lưu trữ tên của các peer cần loại bỏ.
                    var peersToRemove = new List<string>();

                    // Duyệt qua danh sách _activePeers.
                    foreach (var entry in _activePeers.ToList())
                    {
                        // Lấy tên peer và thông tin peer từ mỗi entry.
                        var peerName = entry.Key;
                        var peerInfo = entry.Value;

                        // Bỏ qua nếu peer là cục bộ và có tên trùng với tên người dùng cục bộ.
                        if (peerInfo.IsLocal && peerInfo.UserName == _localUserName) continue;

                        // Kiểm tra xem thời gian kể từ lần heartbeat cuối cùng có vượt quá 30 giây không.
                        if ((now - peerInfo.LastHeartbeat).TotalSeconds > 90) // Increased timeout to 90 seconds
                        {
                            // Ghi log thông báo rằng peer này sẽ bị đánh dấu để loại bỏ.
                            Debug.WriteLine($"[CleanupDisconnectedPeers] Đánh dấu người dùng để xóa: {peerName}, Hoạt động lần cuối: {peerInfo.LastHeartbeat}");
                            // Thêm tên peer vào danh sách cần loại bỏ.
                            peersToRemove.Add(peerName);
                        }
                    }

                    // Duyệt qua danh sách các peer cần loại bỏ.
                    foreach (var peerNameToRemove in peersToRemove)
                    {
                        // Gọi phương thức để đánh dấu peer là đã ngắt kết nối và thực hiện các hành động liên quan.
                        MarkPeerDisconnected(peerNameToRemove);
                    }

                    // Khóa truy cập vào danh sách _processedBroadcastMessageIds để đảm bảo an toàn luồng.
                    lock (_processedBroadcastMessageIds)
                    {
                        // Lấy danh sách các ID tin nhắn broadcast cũ hơn 60 giây so với thời điểm hiện tại.
                        var oldIds = _processedBroadcastMessageIds.Where(id => (now - DateTime.UtcNow).TotalSeconds > 60).ToList();
                        // Duyệt qua danh sách các ID cũ và loại bỏ chúng khỏi danh sách đã xử lý.
                        foreach (var id in oldIds)
                        {
                            _processedBroadcastMessageIds.Remove(id);
                        }
                    }

                    // Chờ 10 giây trước khi thực hiện lại việc dọn dẹp các peer đã ngắt kết nối.
                    await Task.Delay(10000, cancellationToken).ConfigureAwait(false);
                }
                // Bắt lỗi OperationCanceledException nếu tác vụ bị hủy.
                catch (OperationCanceledException)
                {
                    // Thoát khỏi vòng lặp nếu tác vụ bị hủy.
                    Debug.WriteLine("[CleanupDisconnectedPeers] Tác vụ đã bị hủy");
                    break;
                }
                // Bắt bất kỳ ngoại lệ nào khác xảy ra trong quá trình dọn dẹp.
                catch (Exception ex)
                {
                    // Ghi log thông báo lỗi.
                    Debug.WriteLine($"[CleanupDisconnectedPeers] Lỗi: {ex.Message}");
                    // Nếu không có yêu cầu hủy bỏ, chờ 1 giây trước khi tiếp tục vòng lặp.
                    if (!cancellationToken.IsCancellationRequested) await Task.Delay(1000, cancellationToken).ConfigureAwait(false);
                }
            }
        }

        private void MarkPeerDisconnected(string peerName)
        {
            // Thử loại bỏ peer khỏi danh sách _activePeers.
            if (_activePeers.TryRemove(peerName, out PeerInfo removedPeer))
            {
                // Giải phóng tài nguyên của peer đã bị loại bỏ.
                removedPeer.Dispose();
                // Ghi log thông báo rằng peer đã được đánh dấu là ngắt kết nối và đã xóa.
                Debug.WriteLine($"[NetworkService] Người dùng {peerName} đã được đánh dấu là ngắt kết nối và đã xóa. Lần cuối: {removedPeer.EndPoint}");
                // Gọi sự kiện PeerDisconnected để thông báo về việc một peer đã ngắt kết nối.
                PeerDisconnected?.Invoke(this, new PeerDiscoveryEventArgs(peerName, removedPeer.EndPoint));
            }
        }

        protected virtual void OnMessageReceived(ChatMessage message)
        {
            // Gọi sự kiện MessageReceived để thông báo về một tin nhắn mới nhận được.
            MessageReceived?.Invoke(this, new MessageReceivedEventArgs(message));
        }

        public IEnumerable<string> GetActivePeerNames()
        {
            // Lấy danh sách tên của tất cả các peer đang hoạt động, loại trừ tên người dùng cục bộ,
            // sắp xếp theo tên và chuyển đổi thành một danh sách.
            var peers = _activePeers.Keys
                .Where(name => name != _localUserName)
                .OrderBy(n => n)
                .ToList();
            // Ghi log danh sách các peer đang hoạt động được trả về.
            Debug.WriteLine($"[GetActivePeerNames] Đang trả về người dùng: {string.Join(", ", peers)}");
            // Trả về danh sách tên các peer đang hoạt động.
            return peers;
        }

        public void Dispose()
        {
            // Ghi log thông báo bắt đầu quá trình giải phóng tài nguyên.
            Debug.WriteLine("[NetworkService] Đang giải phóng...");
            // Hủy bỏ bất kỳ tác vụ đang chạy nào thông qua cancellation token.
            _cancellationTokenSource?.Cancel();

            try
            {
                // Dừng TCP listener nếu nó đang chạy.
                _tcpListener?.Stop();
                Debug.WriteLine("[NetworkService] TCP Listener đã dừng.");

                // Tạo một mảng các tác vụ nền.
                Task[] tasks = { _udpListenTask, _tcpListenTask, _heartbeatTask, _peerCleanupTask };
                // Lọc ra các tác vụ đang chạy (không phải null, chưa hoàn thành, chưa bị hủy hoặc lỗi).
                var runningTasks = tasks.Where(t => t != null && !t.IsCompleted && !t.IsCanceled && !t.IsFaulted).ToArray();
                // Chờ tất cả các tác vụ đang chạy hoàn thành trong vòng 5 giây.
                if (runningTasks.Any())
                {
                    Task.WaitAll(runningTasks, TimeSpan.FromSeconds(5)); // Increased timeout for tasks to finish
                }
                Debug.WriteLine("[NetworkService] Các tác vụ nền đã được chờ hoặc đã hoàn thành.");
            }
            // Bắt lỗi AggregateException có thể xảy ra nếu có nhiều lỗi trong quá trình chờ các tác vụ.
            catch (AggregateException ex)
            {
                // Ghi log thông tin chi tiết của từng lỗi bên trong.
                foreach (var innerEx in ex.InnerExceptions)
                {
                    Debug.WriteLine($"[NetworkService] Lỗi trong quá trình tắt tác vụ: {innerEx.Message}");
                }
            }
            // Bắt bất kỳ ngoại lệ chung nào khác xảy ra trong quá trình giải phóng.
            catch (Exception ex)
            {
                Debug.WriteLine($"[NetworkService] Lỗi chung trong quá trình tắt máy: {ex.Message}");
            }
            finally
            {
                // Đảm bảo UDP client được đóng và giải phóng.
                if (_udpClient != null)
                {
                    try
                    {
                        _udpClient.Client.Shutdown(SocketShutdown.Both); // Shutdown socket gracefully
                        _udpClient.Close();
                        _udpClient.Dispose();
                        _udpClient = null; // Set to null after disposal
                        Debug.WriteLine("[NetworkService] UDP Client đã đóng và giải phóng.");
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[NetworkService] Lỗi khi đóng/giải phóng UDP client: {ex.Message}");
                    }
                }

                // Giải phóng tài nguyên của tất cả các peer đang hoạt động.
                foreach (var peer in _activePeers.Values)
                {
                    peer.Dispose();
                }
                // Xóa danh sách các peer đang hoạt động.
                _activePeers.Clear();
                Debug.WriteLine("[NetworkService] Các kết nối peer đang hoạt động đã được giải phóng và danh sách đã được xóa.");

                // Giải phóng cancellation token source.
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null; // Set to null after disposal
                Debug.WriteLine("[NetworkService] Giải phóng hoàn tất.");
            }
        }
    }

    public class PeerInfo : IDisposable
    {
        public string UserName { get; }
        public IPEndPoint EndPoint { get; set; }
        public DateTime LastHeartbeat { get; set; }
        public TcpClient TcpClient { get; private set; }
        public bool IsLocal { get; }

        public PeerInfo(string userName, IPEndPoint endPoint, bool isLocal = false)
        {
            UserName = userName;
            EndPoint = endPoint;
            LastHeartbeat = DateTime.UtcNow;
            IsLocal = isLocal;
        }

        public void SetTcpClient(TcpClient client)
        {
            // Kiểm tra nếu đã có một TcpClient khác và khác với client mới.
            if (TcpClient != null && TcpClient != client)
            {
                try
                {
                    // Đóng và giải phóng TcpClient cũ.
                    Debug.WriteLine($"[PeerInfo] Giải phóng TcpClient cũ cho {UserName}.");
                    TcpClient.Close();
                    TcpClient.Dispose();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[PeerInfo] Lỗi khi giải phóng TcpClient cũ cho {UserName}: {ex.Message}");
                }
            }
            // Gán TcpClient mới.
            TcpClient = client;
        }

        public void Dispose()
        {
            try
            {
                // Đóng và giải phóng TcpClient nếu nó tồn tại.
                if (TcpClient != null)
                {
                    Debug.WriteLine($"[PeerInfo] Giải phóng TcpClient cho {UserName} trong PeerInfo.Dispose.");
                    TcpClient.Close();
                    TcpClient.Dispose();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[PeerInfo] Lỗi khi giải phóng TcpClient cho {UserName} trong PeerInfo.Dispose: {ex.Message}");
            }
            // Đặt TcpClient về null để tránh sử dụng sau khi đã giải phóng.
            TcpClient = null;
        }
    }
}