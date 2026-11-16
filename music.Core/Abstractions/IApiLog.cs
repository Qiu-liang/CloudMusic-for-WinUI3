namespace music.Core.Abstractions
{
    public interface IApiLog
    {
        void Write(string message);
    }

    public sealed class NullApiLog : IApiLog
    {
        public static NullApiLog Instance { get; } = new();

        private NullApiLog() { }

        public void Write(string message) { }
    }
}
