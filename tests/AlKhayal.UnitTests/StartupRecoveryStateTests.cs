using AlKhayal.UI;
using Xunit;

namespace AlKhayal.UnitTests
{
    public sealed class StartupRecoveryStateTests
    {
        [Fact]
        public void Failure_is_recorded_and_user_exit_does_not_require_confirmation()
        {
            var state = new StartupRecoveryState();

            state.RecordFailure();

            Assert.True(state.Failed);
            Assert.False(state.ShouldConfirmExit(true));
        }

        [Fact]
        public void Retry_is_single_use_until_the_attempt_finishes()
        {
            var state = new StartupRecoveryState();
            state.RecordFailure();

            Assert.True(state.BeginRetry());
            Assert.False(state.BeginRetry());
            state.Complete();
            Assert.False(state.Failed);
        }

        [Fact]
        public void Close_request_bypasses_normal_exit_confirmation()
        {
            var state = new StartupRecoveryState();

            state.RequestClose();

            Assert.False(state.ShouldConfirmExit(true));
            Assert.True(state.CloseRequested);
        }

        [Fact]
        public void Successful_recovery_clears_failure_state()
        {
            var state = new StartupRecoveryState();
            state.RecordFailure();
            state.BeginRetry();

            state.Complete();

            Assert.False(state.Failed);
            Assert.False(state.Retrying);
        }
    }
}
