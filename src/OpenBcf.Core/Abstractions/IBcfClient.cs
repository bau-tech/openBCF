namespace OpenBcf.Core.Abstractions;

public interface IBcfClient
{
    string Name { get; }
    void Initialize();
    void Connect();
    void Disconnect();
}
