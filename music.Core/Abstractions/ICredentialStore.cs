namespace music.Core.Abstractions
{
    public interface ICredentialStore
    {
        string Load();
        void Save(string cookie);
        void Clear();
    }
}
