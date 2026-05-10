namespace password_break_server.Services
{
    public class ServerRunState
    {
        private readonly object _lock = new();

        public bool IsRunning { get; private set; }

        public event Action<bool>? StateChanged;

        public void Toggle()
        {
            lock (_lock)
            {
                IsRunning = !IsRunning;
            }

            StateChanged?.Invoke(IsRunning);
        }

        public void Start()
        {
            lock (_lock)
            {
                IsRunning = true;
            }

            StateChanged?.Invoke(true);
        }

        public void Stop()
        {
            lock (_lock)
            {
                IsRunning = false;
            }

            StateChanged?.Invoke(false);
        }
    }
}