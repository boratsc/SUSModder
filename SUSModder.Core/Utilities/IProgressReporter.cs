namespace SUSModder.Core.Utilities
{
    public interface IProgressReporter
    {
        void Report(int percent, string? message = null);
    }
}
