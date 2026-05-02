namespace yugiho_tools.Application.Helpers;

public static class CardImage
{
    private const string BaseUrl = "https://www.basededatostea.xyz/img/lmfv";

    public static string Url(int cardId) => $"{BaseUrl}/{cardId}.jpg";
}
