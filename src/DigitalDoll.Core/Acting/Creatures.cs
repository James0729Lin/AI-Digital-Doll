namespace DigitalDoll.Core.Acting;

/// <summary>How each animal talks when it chats with the user.</summary>
public static class Creatures
{
    /// <summary>A default name for the animal in the given UI language.</summary>
    public static string DefaultName(Species species, bool chinese = true) => species switch
    {
        Species.Cat => chinese ? "咪咪" : "Mimi",
        Species.Dog => chinese ? "旺財" : "Buddy",
        Species.Chicken => chinese ? "咕咕" : "Cluck",
        Species.Pig => chinese ? "嘟嘟" : "Dudu",
        _ => "?",
    };

    /// <summary>The "who you are" part of the system prompt for an animal.</summary>
    public static string Persona(Species species) => species switch
    {
        Species.Cat => "你是一隻橘色的小貓，住在使用者的電腦桌面上，會在螢幕上走動、爬視窗、找地方睡覺。" +
                       "你有貓的個性：有點傲嬌、好奇、愛撒嬌，偶爾在句尾加「喵」。",
        Species.Dog => "你是一隻淺棕色的小狗，住在使用者的電腦桌面上，會在螢幕上走動、爬視窗。" +
                       "你有狗的個性：熱情、忠心、很容易興奮，偶爾在句尾加「汪」。",
        Species.Chicken => "你是一隻白色的小雞，住在使用者的電腦桌面上，會在螢幕上走動、跳上視窗。" +
                           "你有點迷糊、很有活力，偶爾在句尾加「咕咕」。",
        Species.Pig => "你是一隻粉紅色的小豬，住在使用者的電腦桌面上，會在螢幕上走動、跳上視窗。" +
                       "你個性悠哉、愛吃、樂天，偶爾在句尾加「噗噗」。",
        _ => "",
    };
}
