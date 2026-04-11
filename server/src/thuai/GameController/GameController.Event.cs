namespace Thuai.GameController;

public partial class GameController
{
    public class AfterPlayerConnectEventArgs : EventArgs
    {
        public Guid SocketId { get; init; }
        public string Token { get; init; } = "";
    }

    public event EventHandler<AfterPlayerConnectEventArgs>? AfterPlayerConnectEvent;
}
