namespace password_break_client;

public class ClientConfig
{
    public int? MaxDegreeOfParallelism { get; set; }
}

public class ClientSettingsRoot
{
    public ClientConfig? Client { get; set; }
}
