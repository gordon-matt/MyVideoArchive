namespace MyVideoArchive.Services;

public interface ISecretsManager
{
    IEnumerable<Secret> Secrets { get; set; }

    string GetSecret(string secretName);
}