namespace AIGarage.Mobile.Services;

public sealed record BrokerCredentials(string Username, string Password, string HmacKeyHex)
{
    public override string ToString() => "BrokerCredentials { redacted }";

    public byte[] GetKey()
    {
        if (string.IsNullOrWhiteSpace(Username) || string.IsNullOrWhiteSpace(Password))
            throw new ArgumentException("Vyplňte MQTT používateľa aj heslo.");
        if (HmacKeyHex is null || HmacKeyHex.Length != 64)
            throw new ArgumentException("HMAC kľúč musí obsahovať 64 hexadecimálnych znakov.");
        try { return Convert.FromHexString(HmacKeyHex); }
        catch (FormatException) { throw new ArgumentException("Neplatný HMAC kľúč."); }
    }
}
