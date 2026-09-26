using WindowsAIAssistant.Core.Enums;

namespace WindowsAIAssistant.Application.Voice.Text;

/// <summary>
/// The ordered table of everything the assistant can understand without asking a model.
/// <para>
/// Order is the priority mechanism. Assistant control comes first so "stop" and "cancel" are
/// never swallowed by a broader pattern; local device commands come before the web and file
/// searches; and the general application-launch pattern comes last so it cannot shadow
/// "open settings" or "open downloads". The AI question rule sits at the bottom because it
/// matches almost any remaining sentence.
/// </para>
/// </summary>
internal static class IntentRuleSet
{
    private const string YouTubeProvider = "YouTube";
    private const string GitHubProvider = "GitHub";
    private const string StackOverflowProvider = "StackOverflow";

    private static IReadOnlyDictionary<string, string> ProviderParameters(string provider) =>
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [Core.Models.Voice.VoiceCommand.SearchProviderParameter] = provider
        };

    public static IReadOnlyList<IntentRule> Create() =>
    [
        .. AssistantControlRules(),
        .. VolumeRules(),
        .. TimeAndDateRules(),
        BatteryRules(),
        .. SystemInformationRules(),
        ScreenshotRules(),
        .. ClipboardRules(),
        SettingsRules(),
        KnownFolderRules(),
        .. SiteSearchRules(),
        WebSearchRules(),
        FileSearchRules(),
        .. ApplicationRules(),
        AiQuestionRules()
    ];

    private static IReadOnlyList<IntentRule> AssistantControlRules() =>
    [
        new IntentRule(
            AssistantIntent.Cancel,
            [
                @"^cancel$",
                @"^cancel (?:that|it|this|the command|everything)$",
                @"^never ?mind$",
                @"^forget it$",
                @"^abort$"
            ]),
        ConfirmRules(),
        StopSpeakingRules(),
        RepeatResponseRules(),
        SpeakTextRules(),
        StopListeningRules(),
        StartListeningRules()
    ];

    private static IntentRule ConfirmRules() => new(
        AssistantIntent.ConfirmCommand,
        [
            @"^yes$",
            @"^yep$",
            @"^yeah$",
            @"^confirm$",
            @"^do it$",
            @"^go ahead$",
            @"^proceed$",
            @"^that'?s right$",
            @"^that'?s correct$",
            @"^correct$"
        ],
        confidence: 0.9);

    private static IntentRule StopSpeakingRules() => new(
        AssistantIntent.StopSpeaking,
        [
            @"^stop talking$",
            @"^stop speaking$",
            @"^stop reading$",
            @"^be quiet$",
            @"^silence$",
            @"^quiet$",
            @"^stop$",
            @"^shut up$",
            @"^enough$"
        ]);

    private static IntentRule RepeatResponseRules() => new(
        AssistantIntent.RepeatResponse,
        [
            @"^repeat that$",
            @"^say that again$",
            @"^(?:repeat|say) (?:that|it|the last response) again$",
            @"^come again$",
            @"^repeat$",
            @"^what did you say$"
        ]);

    private static IntentRule SpeakTextRules() => new(
        AssistantIntent.SpeakText,
        [
            @"^read (?:that|it|the last response) (?:aloud|out loud)$",
            @"^speak (?:that|it|the last response) (?:aloud|out loud)$",
            @"^say (?:that|it|the last response) (?:aloud|out loud)$"
        ]);

    private static IntentRule StopListeningRules() => new(
        AssistantIntent.StopListening,
        [
            @"^stop listening$",
            @"^(?:stop|end) (?:the )?(?:listening|recognition|voice assistant)$",
            @"^that's all$",
            @"^i'?m done$"
        ]);

    private static IntentRule StartListeningRules() => new(
        AssistantIntent.StartListening,
        [
            @"^start listening$",
            @"^(?:begin|start) (?:the )?voice assistant$",
            @"^listen$"
        ]);

    private static IReadOnlyList<IntentRule> VolumeRules() =>
    [
        // Changing the output level is a system setting, so every rule in this group is marked
        // as needing confirmation. The recognizer is the only place that classifies a phrase,
        // and leaving these at the default "safe" would let a misheard command change the
        // volume with no confirmation at all.
        new IntentRule(
            AssistantIntent.SetVolume,
            [
                @"^(?:set|change) (?:the )?volume to (?<volume>\d{1,3})\s*(?:percent|%)?$",
                @"^(?:set|change) (?:the )?volume (?<volume>\d{1,3})\s*(?:percent|%)?$",
                @"^(?:set|change) (?:the )?volume to (?<volume>\d{1,3})$",
                @"^volume (?<volume>\d{1,3})\s*(?:percent|%)?$",
                @"^(?<volume>\d{1,3})\s*(?:percent|%)$"
            ],
            safetyLevel: ActionSafetyLevel.ConfirmationRequired,
            requiresConfirmation: true),
        new IntentRule(
            AssistantIntent.IncreaseVolume,
            [
                @"^volume up$",
                @"^(?:increase|raise|turn up|bump) (?:the )?(?:volume|sound|audio)$",
                @"^turn it up$",
                @"^turn (?:the )?(?:volume|sound) up$",
                @"^make (?:it|the volume|the sound) louder$",
                @"^louder$"
            ],
            safetyLevel: ActionSafetyLevel.ConfirmationRequired,
            requiresConfirmation: true),
        new IntentRule(
            AssistantIntent.DecreaseVolume,
            [
                @"^volume down$",
                @"^(?:decrease|lower|turn down|drop) (?:the )?(?:volume|sound|audio)$",
                @"^turn it down$",
                @"^turn (?:the )?(?:volume|sound) down$",
                @"^make (?:it|the volume|the sound) quieter$",
                @"^quieter$"
            ],
            safetyLevel: ActionSafetyLevel.ConfirmationRequired,
            requiresConfirmation: true),
        new IntentRule(
            AssistantIntent.MuteVolume,
            [
                @"^mute$",
                @"^mute (?:it|the sound|everything)$",
                @"^mute (?:the )?(?:computer|pc|system|sound|audio|volume|speaker)$"
            ],
            safetyLevel: ActionSafetyLevel.ConfirmationRequired,
            requiresConfirmation: true),
        new IntentRule(
            AssistantIntent.UnmuteVolume,
            [
                @"^unmute$",
                @"^unmute (?:it|the sound|everything)$",
                @"^unmute (?:the )?(?:computer|pc|system|sound|audio|volume|speaker)$",
                @"^turn (?:the )?sound on$"
            ],
            safetyLevel: ActionSafetyLevel.ConfirmationRequired,
            requiresConfirmation: true)
    ];
    private static IReadOnlyList<IntentRule> TimeAndDateRules() =>
    [
        new IntentRule(
            AssistantIntent.GetTime,
            [
                @"^what time is it$",
                @"^what'?s the time$",
                @"^what is the time$",
                @"^what'?s the current time$",
                @"^tell me the time$",
                @"^current time$",
                @"^the time$"
            ]),
        new IntentRule(
            AssistantIntent.GetDate,
            [
                @"^what'?s (?:today'?s |the )?date$",
                @"^what is (?:today'?s |the )?date$",
                @"^what'?s today$",
                @"^today'?s date$",
                @"^what day is it$",
                @"^what'?s the day$",
                @"^tell me the date$",
                @"^what is the day of the week$"
            ])
    ];

    private static IntentRule BatteryRules() => new(
        AssistantIntent.GetBatteryStatus,
        [
            @"^what'?s my battery$",
            @"^what is my battery$",
            @"^what'?s my battery (?:\w+)$",
            @"^battery (?:\w+)$",
            @"^how much battery (?:is )?(?:left|do i have|remaining)$",
            @"^how much battery do i have left$",
            @"^is (?:my |it )?(?:laptop|computer|battery|it) charging$",
            @"^am i charging$"
        ]);

    private static IReadOnlyList<IntentRule> SystemInformationRules() =>
    [
        new IntentRule(
            AssistantIntent.GetMemoryUsage,
            [
                @"^how much (?:ram|memory) (?:do i have|am i using|is being used|do i have left|is available|is free)$",
                @"^how much (?:ram|memory) is being used$",
                @"^memory usage$",
                @"^ram usage$"
            ]),
        new IntentRule(
            AssistantIntent.GetStorageUsage,
            [
                @"^how much (?:disk )?space (?:is available|do i have|is left|is free)$",
                @"^how much storage (?:do i have|is available|is left)$",
                @"^disk space$"
            ]),
        new IntentRule(
            AssistantIntent.GetSystemInformation,
            [
                @"^system info(?:rmation)?$",
                @"^(?:get|show|tell me) (?:the )?system info(?:rmation)?$",
                @"^what'?s my computer name$",
                @"^what is my computer name$",
                @"^computer name$",
                @"^what version of windows am i (?:using|running|on)$",
                @"^how many (?:processors|cores|cpus) do i have$",
                @"^what'?s my pc$"
            ])
    ];

    private static IntentRule ScreenshotRules() => new(
        AssistantIntent.TakeScreenshot,
        [
            @"^(?:take|capture|grab|get) (?:a |my |the )?screenshot(?: (?:of|on) (?:my |the )?(?:screen|display))?$",
            @"^screenshot$",
            @"^capture (?:my |the )?(?:screen|display)$"
        ]);

    private static IReadOnlyList<IntentRule> ClipboardRules() =>
    [
        new IntentRule(
            AssistantIntent.SummarizeClipboard,
            [
                @"^(?:summari[sz]e|explain) (?:my |the |what'?s on )?clipboard(?: (?:content|text))?$",
                @"^(?:summari[sz]e|explain) (?:what'?s on |what is on |what'?s in )?(?:my |the )?clipboard(?: (?:content|text))?$",
                @"^what does my clipboard say$"
            ]),
        new IntentRule(
            AssistantIntent.ReadClipboard,
            [
                @"^read (?:my |the )?clipboard(?: (?:content|text))?$",
                @"^what'?s on my clipboard$",
                @"^what is on my clipboard$",
                @"^(?:show|tell) me (?:my |the )?clipboard(?: (?:content|text))?$",
                @"^clipboard$"
            ])
    ];

    private static IntentRule SettingsRules() => new(
        AssistantIntent.OpenSettings,
        [
            @"^open (?:windows )?settings$",
            @"^open (?:the )?control panel$",

            // The trailing "settings" is optional because "open wifi" and "open wifi settings"
            // are the same request, and requiring the word would make the shorter phrasing fail.
            @"^open (?<setting>bluetooth|bluetooth settings|wifi|wireless|network(?: and internet)?|internet|display|sound|audio|volume|privacy|security|windows update|update|notifications|apps|default apps|power|battery|storage|printers?|account|personalization|about|homepage|background|device manager)(?: settings)?$",
            @"^show (?:me )?(?:the )?(?<setting>bluetooth|wifi|display|sound|privacy|windows update)(?: settings)?$"
        ]);

    private static IntentRule KnownFolderRules() => new(
        AssistantIntent.OpenFolder,
        [
            @"^open (?:my |the )?(?<folder>downloads?|documents?|desktop|pictures?|photos?|music|videos?)$",
            @"^show (?:me )?(?:my |the )?(?<folder>downloads?|documents?|desktop|pictures?|music|videos?)$",
            @"^open (?:my )?(?<folder>downloads?|documents?|desktop|pictures?|music|videos?) folder$"
        ]);

    private static IReadOnlyList<IntentRule> SiteSearchRules() =>
    [
        new IntentRule(
            AssistantIntent.YouTubeSearch,
            [
                @"^search (?:on )?youtube for (?<query>.+)$",
                @"^youtube (?:search )?(?:for )?(?<query>.+)$",
                @"^look up (?<query>.+) on youtube$"
            ],
            constantParameters: ProviderParameters(YouTubeProvider)),
        new IntentRule(
            AssistantIntent.WebSearch,
            [
                @"^search (?:on )?github for (?<query>.+)$",
                @"^github (?:search )?(?:for )?(?<query>.+)$"
            ],
            confidence: 0.9,
            constantParameters: ProviderParameters(GitHubProvider)),
        new IntentRule(
            AssistantIntent.WebSearch,
            [
                @"^search (?:on )?stack ?overflow for (?<query>.+)$"
            ],
            confidence: 0.9,
            constantParameters: ProviderParameters(StackOverflowProvider))
    ];

    private static IntentRule WebSearchRules() => new(
        AssistantIntent.WebSearch,
        [
            @"^search (?:google|the web|the internet) for (?<query>.+)$",
            @"^google (?:search )?(?:for )?(?<query>.+)$",
            @"^search (?:the web|the internet) for (?<query>.+)$",
            @"^web search (?<query>.+)$",
            @"^search for (?<query>.+)$",
            @"^look up (?<query>.+)$"
        ]);

    private static IntentRule FileSearchRules() => new(
        AssistantIntent.FileSearch,
        [
            @"^search (?:my |the )?(?:files|documents|folders|computer|pc|laptop) for (?<query>.+)$",
            @"^show (?:me )?(?<query>pdfs?|documents?|files?|photos?|images?) (?:about|on|for|related to|matching) (?<rest>.+)$",
            @"^find (?:me )?(?<query>.+?) (?:documents?|files?|pdfs?) (?:about|on|for|related to|matching) (?<rest>.+)$",
            @"^look for (?<query>.+)$",
            @"^find (?:me )?(?:my |the |all )?(?<query>.+)$"
        ]);

    private static IReadOnlyList<IntentRule> ApplicationRules() =>
    [
        new IntentRule(
            AssistantIntent.CloseApplication,
            [
                @"^close (?<application>.+)$",
                @"^quit (?<application>.+)$",
                @"^exit (?<application>.+)$"
            ],
            confidence: 0.9,
            safetyLevel: ActionSafetyLevel.ConfirmationRequired,
            requiresConfirmation: true),
        new IntentRule(
            AssistantIntent.OpenApplication,
            [
                @"^(?:please )?(?:can you|could you|would you) (?:please )?(?:open|launch|start|run) (?<application>.+)$",
                @"^(?:open|launch|start|run) (?<application>.+)$"
            ])
    ];

    private static IntentRule AiQuestionRules() => new(
        AssistantIntent.AIQuestion,
        [
            @"^ask (?:the )?(?:ai|assistant) (?<text>.+)$",
            @"^tell me about (?<text>.+)$",
            @"^(?:tell me|explain|describe|summari[sz]e) (?<text>.+)$",
            @"^how (?:do i|does|to|can i|should i) (?<text>.+)$",
            @"^why (?:is|are|do|does) (?<text>.+)$",
            @"^what (?:is|are) (?<text>.+)$",
            @"^who (?:is|are) (?<text>.+)$",
            @"^where (?:is|can i find) (?<text>.+)$",
            @"^when (?:is|do|does) (?<text>.+)$",
            @"^i need (?:help|to know) (?:with|about|on) (?<text>.+)$"
        ],
        confidence: 0.75);
}
