using System.Text.Json.Nodes;

namespace WaveTechFluxerTTS.Fluxer;

public static class CommandDefinitions
{
    public static JsonNode[] AllCommands =>
    [
        ..TtsCommands,
        ..DalleCommands,
        ..DistributeCommands,
        ..SecretSantaCommands
    ];

    public static JsonNode[] TtsCommands =>
    [
        Cmd("tts", "TTS voice controls", [
            Sub("stats", "Performance metrics"),
            Sub("status", "Voice channel status"),
            Sub("diagnostics", "System diagnostics"),
            Sub("disconnect", "Force disconnect from voice", defaultMemberPermissions: "32"),
            Sub("clear", "Clear TTS queue", defaultMemberPermissions: "32")
        ])
    ];

    public static JsonNode[] DalleCommands =>
    [
        Cmd("image", "Generate AI images with DALL-E 3", [
            Opt("prompt", 3, "Describe the image", required: true),
            Opt("size", 3, "Image size", required: false, choices: ["1024x1024", "1792x1024", "1024x1792"]),
            Opt("quality", 3, "Quality", required: false, choices: ["standard", "hd"]),
            Opt("private", 5, "Private response", required: false)
        ])
    ];

    public static JsonNode[] DistributeCommands =>
    [
        Cmd("distribute", "Share files with Secret Santa participants", [
            Sub("upload", "Upload and distribute a file", [
                Opt("attachment", 11, "File to upload", required: true),
                Opt("required_by", 6, "User who required this file", required: false)
            ]),
            Sub("list", "List shared files"),
            Sub("browse", "Browse shared files"),
            Sub("get", "Download a file by name", [Opt("filename", 3, "File name", required: true)]),
            Sub("remove", "Remove a file (mod)", [Opt("filename", 3, "File name", required: true)])
        ])
    ];

    public static JsonNode[] SecretSantaCommands =>
    [
        Cmd("ss", "Secret Santa event", [
            Sub("start", "Start event", [
                Opt("message", 3, "Signup message text (if no message_id)", required: false),
                Opt("message_id", 3, "Existing signup message ID to use", required: false),
                Opt("role_id", 3, "Participant role ID (optional)", required: false)
            ]),
            Sub("status", "Event dashboard"),
            Sub("shuffle", "Assign pairs and DM"),
            Sub("stop", "End and archive event"),
            Sub("ask_giftee", "Ask giftee anonymously", [Opt("question", 3, "Your question", required: true)]),
            Sub("submit_gift", "Log your gift", [Opt("gift", 3, "What you gave", required: true)]),
            Sub("giftee", "View giftee wishlist"),
            Sub("oversight", "Mod: view gifts/comms", [
                Opt("view", 3, "gifts|comms|all", required: false, choices: ["gifts", "comms", "all"])
            ]),
            Sub("history", "Past year summary", [Opt("year", 4, "Year", required: false)]),
            Sub("edit_gift", "Edit gift in archive", [
                Opt("year", 4, "Year", required: true),
                Opt("gift_description", 3, "Updated gift text", required: true)
            ]),
            Sub("archive", "Archive maintenance (mod)", [
                Opt("action", 3, "delete|restore|backups", required: true, choices: ["delete", "restore", "backups"]),
                Opt("year", 4, "Year", required: false)
            ]),
            Sub("user_history", "Mod: user SS history", [
                Opt("user", 6, "User to look up", required: true)
            ]),
            Sub("wishlist", "Wishlist", [
                Opt("action", 3, "add|remove|view|clear", required: true),
                Opt("item", 3, "Item text or number", required: false)
            ])
        ])
    ];

    private static JsonObject Cmd(string name, string description, JsonNode[]? options = null, string? defaultMemberPermissions = null)
    {
        var o = new JsonObject { ["name"] = name, ["description"] = description, ["type"] = 1 };
        if (options is { Length: > 0 }) o["options"] = new JsonArray(options);
        if (defaultMemberPermissions is not null) o["default_member_permissions"] = defaultMemberPermissions;
        return o;
    }

    private static JsonObject Sub(string name, string description, JsonNode[]? options = null, string? defaultMemberPermissions = null)
    {
        var o = new JsonObject { ["name"] = name, ["description"] = description, ["type"] = 1 };
        if (options is { Length: > 0 }) o["options"] = new JsonArray(options);
        if (defaultMemberPermissions is not null) o["default_member_permissions"] = defaultMemberPermissions;
        return o;
    }

    private static JsonObject Opt(string name, int type, string description, bool required, string[]? choices = null)
    {
        var o = new JsonObject
        {
            ["name"] = name,
            ["description"] = description,
            ["type"] = type,
            ["required"] = required
        };
        if (choices is not null)
        {
            o["choices"] = new JsonArray(choices.Select(c => (JsonNode)new JsonObject
            {
                ["name"] = c,
                ["value"] = c
            }).ToArray());
        }
        return o;
    }
}
