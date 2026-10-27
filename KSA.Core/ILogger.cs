namespace KSA.Core
{
    /// <summary>
    /// Platform-neutral logging. Core never writes to a console or Unity directly;
    /// the host (KSA.KSP) supplies an implementation.
    /// </summary>
    public interface ILogger
    {
        void Info(string message);
        void Warn(string message);
        void Error(string message);
    }
}
