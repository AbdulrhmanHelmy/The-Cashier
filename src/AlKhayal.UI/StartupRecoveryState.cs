namespace AlKhayal.UI
{
    internal sealed class StartupRecoveryState
    {
        public bool Failed { get; private set; }
        public bool Retrying { get; private set; }
        public bool CloseRequested { get; private set; }

        public void RecordFailure()
        {
            Failed = true;
            Retrying = false;
        }

        public bool BeginRetry()
        {
            if (!Failed || Retrying || CloseRequested)
                return false;

            Retrying = true;
            Failed = false;
            return true;
        }

        public void Complete()
        {
            Failed = false;
            Retrying = false;
        }

        public void RequestClose()
        {
            CloseRequested = true;
            Retrying = false;
        }

        public bool ShouldConfirmExit(bool userClosing)
        {
            return userClosing && !CloseRequested && !Failed;
        }
    }
}
