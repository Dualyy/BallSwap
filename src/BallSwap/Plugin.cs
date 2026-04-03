using BepInEx;
using BepInEx.Configuration;
using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace BallSwap;

public enum CountdownDuration { FiveSeconds = 5, TenSeconds = 10 }

[BepInAutoPlugin]
public partial class Plugin : BaseUnityPlugin
{
    public static Plugin Instance = null!;
    public static ConfigEntry<bool> Enabled = null!;
    public static ConfigEntry<int> MinInterval = null!;
    public static ConfigEntry<int> MaxInterval = null!;
    public static ConfigEntry<bool> ShowCountdown = null!;
    public static ConfigEntry<CountdownDuration> CountdownDuration = null!;

    bool IsFixed => MinInterval.Value == MaxInterval.Value;
    float _swapAt = -1f;
    bool _countdownVisible = false;

    private void Awake()
    {
        Instance = this;

        Enabled = Config.Bind("General", "Enable", true, "Enable periodic ball swapping");
        MinInterval = Config.Bind("General", "MinInterval", 20, new ConfigDescription("Minimum seconds between swaps. Set equal to MaxInterval for a fixed interval.", new AcceptableValueRange<int>(10, 180)));
        MaxInterval = Config.Bind("General", "MaxInterval", 40, new ConfigDescription("Maximum seconds between swaps. Set equal to MinInterval for a fixed interval.", new AcceptableValueRange<int>(10, 180)));
        ShowCountdown = Config.Bind("General", "ShowCountdown", true, "Show a countdown before the swap.");
        CountdownDuration = Config.Bind("General", "CountdownDuration", BallSwap.CountdownDuration.FiveSeconds, "How many seconds before the swap to show the countdown warning.");

        StartCoroutine(SwapRoutine());
    }

    IEnumerator SwapRoutine()
    {
        while (true)
        {
            // Wait for a new game to start
            yield return new WaitUntil(() => IsGameActive());

            // Wait out the intro cinematic + countdown before first swap
            float introEnd = Time.time + 13f;
            yield return new WaitUntil(() => Time.time >= introEnd || !IsGameActive());
            if (!IsGameActive()) continue;

            // Swap loop for this game session
            while (IsGameActive())
            {
                int interval = IsFixed
                    ? MinInterval.Value
                    : UnityEngine.Random.Range(MinInterval.Value, MaxInterval.Value + 1);

                _swapAt = Time.time + interval;
                bool aborted = false;

                if (ShowCountdown.Value)
                {
                    float waitBeforeCountdown = interval - (int)CountdownDuration.Value;
                    if (waitBeforeCountdown > 0)
                    {
                        float waitEnd = Time.time + waitBeforeCountdown;
                        yield return new WaitUntil(() => Time.time >= waitEnd || !IsGameActive());
                        if (!IsGameActive()) { aborted = true; }
                    }

                    if (!aborted)
                    {
                        _countdownVisible = true;

                        while (Time.time < _swapAt)
                        {
                            if (!IsGameActive()) { aborted = true; break; }
                            float timeLeft = _swapAt - Time.time;
                            if (timeLeft <= 10f)
                                BroadcastChatMessage($"Ball swap in {Mathf.CeilToInt(timeLeft)}!");
                            yield return new WaitForSeconds(1f);
                        }

                        _countdownVisible = false;
                    }
                }
                else
                {
                    float waitEnd = Time.time + interval;
                    yield return new WaitUntil(() => Time.time >= waitEnd || !IsGameActive());
                    if (!IsGameActive()) aborted = true;
                }

                _swapAt = -1f;
                if (!aborted) DoSwap();
            }

            // Game ended; clean up for next game
            _countdownVisible = false;
            _swapAt = -1f;
        }
    }

    bool IsGameActive() =>
        Enabled.Value &&
        CourseManager.ServerMatchParticipants != null &&
        CourseManager.ServerMatchParticipants.Count >= 2;

    void DoSwap()
    {
        var participants = CourseManager.ServerMatchParticipants;
        if (participants == null || participants.Count < 2) return;

        var active = participants
            .Where(p => p != null && p.NetworkownBall != null &&
                        p.matchResolution != PlayerMatchResolution.Scored &&
                        p.matchResolution != PlayerMatchResolution.Eliminated)
            .ToList();

        if (active.Count < 2) return;

        var balls = active.Select(p => p.NetworkownBall).ToList();

        // Shuffle until no player keeps their own ball (derangement)
        do
        {
            for (int i = balls.Count - 1; i > 0; i--)
            {
                int j = UnityEngine.Random.Range(0, i + 1);
                (balls[i], balls[j]) = (balls[j], balls[i]);
            }
        } while (Enumerable.Range(0, active.Count).Any(i => balls[i] == active[i].NetworkownBall));

        for (int i = 0; i < active.Count; i++)
        {
            active[i].NetworkownBall = balls[i];
            balls[i].Networkowner = active[i];
            _updateNameTag?.Invoke(balls[i], null);
        }

        BroadcastChatMessage("Balls swapped!");
    }

    static readonly MethodInfo _cmdSendMessage = typeof(TextChatManager)
        .GetMethod("CmdSendMessageInternal", BindingFlags.NonPublic | BindingFlags.Instance);

    static readonly MethodInfo _updateNameTag = typeof(GolfBall)
        .GetMethod("UpdateNameTag", BindingFlags.NonPublic | BindingFlags.Instance);

    void BroadcastChatMessage(string message)
    {
        var chat = TextChatManager.Instance;
        if (chat == null) return;
        _cmdSendMessage?.Invoke(chat, new object[] { message, null });
    }

void OnGUI()
    {
        if (!_countdownVisible) return;

        float timeLeft = _swapAt - Time.time;
        if (timeLeft <= 0) return;

        var style = new GUIStyle(GUI.skin.label)
        {
            fontSize = 48,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
        };
        style.normal.textColor = Color.yellow;

        GUI.Label(
            new Rect(0, Screen.height * 0.15f, Screen.width, 80),
            $"BALL SWAP IN {Mathf.CeilToInt(timeLeft)}!",
            style);
    }
}
