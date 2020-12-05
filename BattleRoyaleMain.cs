using System;
using System.Reflection;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using UnityEngine;
using HarmonyLib;
using Poly.Math;
using BepInEx;
using BepInEx.Configuration;
using PolyTechFramework;
using UnityEngine.Networking;

namespace BattleRoyaleMod
{
    [BepInPlugin(pluginGuid, pluginName, pluginVerson)]
    [BepInProcess("Poly Bridge 2")]
    [BepInDependency(PolyTechMain.PluginGuid, BepInDependency.DependencyFlags.HardDependency)]
    public class BattleRoyaleMain : PolyTechMod
    {

        public const string pluginGuid = "polytech.battle_royale_mod";

        public const string pluginName = "Battle Royale Mod";

        public const string pluginVerson = "1.1.0";

        public static ConfigDefinition modEnableDef = new ConfigDefinition(pluginName, "Enable/Disable Mod");
        public static ConfigDefinition SecondOppDef = new ConfigDefinition(pluginName, "Second Key");
        public static ConfigDefinition AutoMuteDef = new ConfigDefinition(pluginName, "Mute everyone who failed");
        public static ConfigDefinition CreateWhitelistDef = new ConfigDefinition(pluginName, "Enable/Disable Whitelist");
        public static ConfigDefinition AddWhitelistDef = new ConfigDefinition(pluginName, "Add/Remove to/from Whitelist");
        public static ConfigDefinition WhitelistMuteDef = new ConfigDefinition(pluginName, "Remove on mute");
        public static ConfigDefinition SetTimerDef = new ConfigDefinition(pluginName, "Set Timer");
        public static ConfigDefinition TimerScreenDef = new ConfigDefinition(pluginName, "Timer In Middle");

        public static ConfigEntry<bool> mEnabled;

        public static ConfigEntry<KeyboardShortcut> mSecondOpp;

        public static ConfigEntry<KeyboardShortcut> mAutoMute;
        public static bool AutoMuteDown = false;
        public static bool Banning = false;
        public static List<PolyTwitchSuggestion> Failed = new List<PolyTwitchSuggestion>();
        public static UnityWebRequestAsyncOperation currentAction;
        public static int MaxBans;
        public static int BanAmount;

        public static ConfigEntry<KeyboardShortcut> mCreateWhitelist;
        public static bool CreateWhitelistDown = false;
        public static ConfigEntry<KeyboardShortcut> mAddWhitelist;
        public static bool AddWhitelistDown = false;
        public static ConfigEntry<bool> mWhitelistMute;

        public static ConfigEntry<KeyboardShortcut> mSetTimer;
        public static bool SetTimerDown = false;
        public static int TimerSeconds;
        public static bool TimerEnabled;
        public static DateTime BeginingTimer = DateTime.Now;

        public static ConfigEntry<bool> mTimerScreen;
        public static string LastSuggestion;

        public static List<string> whitelist = new List<string>();
        public static bool whitelistEnabled = false;

        public static bool InUpdate = false;

        public static string Key = "";

        public static BattleRoyaleMain instance;

        void Awake()
        {
            if (instance == null) instance = this;

            int order = 0;

            Config.Bind(modEnableDef, true, new ConfigDescription("Controls if the mod is enabled or disabled", null, new ConfigurationManagerAttributes { Order = order }));
            mEnabled = (ConfigEntry<bool>)Config[modEnableDef];
            mEnabled.SettingChanged += onEnableDisable;
            order--;

            mSecondOpp = Config.Bind(SecondOppDef, new KeyboardShortcut(KeyCode.LeftShift), new ConfigDescription("What button triggers the second opperation", null, new ConfigurationManagerAttributes { Order = order }));
            order--;

            mAutoMute = Config.Bind(AutoMuteDef, new KeyboardShortcut(KeyCode.F3), new ConfigDescription("What button auto mutes players who failed", null, new ConfigurationManagerAttributes { Order = order }));
            order--;

            mCreateWhitelist = Config.Bind(CreateWhitelistDef, new KeyboardShortcut(KeyCode.F4), new ConfigDescription("What button enables/disabled the whitelist (+ second key: overwrite whitelist)", null, new ConfigurationManagerAttributes { Order = order }));
            order--;

            mAddWhitelist = Config.Bind(AddWhitelistDef, new KeyboardShortcut(KeyCode.F5), new ConfigDescription("What button adds players to the whitelist (+ second key: remove players)", null, new ConfigurationManagerAttributes { Order = order }));
            order--;

            Config.Bind(WhitelistMuteDef, true, new ConfigDescription("Controls if players get removed from the whitelist if they get muted", null, new ConfigurationManagerAttributes { Order = order }));
            mWhitelistMute = (ConfigEntry<bool>)Config[WhitelistMuteDef];
            order--;

            mSetTimer = Config.Bind(SetTimerDef, new KeyboardShortcut(KeyCode.F6), new ConfigDescription("What button starts/adds to the timer (+ second key: stop the timer)", null, new ConfigurationManagerAttributes { Order = order }));
            order--;

            Config.Bind(TimerScreenDef, true, new ConfigDescription("Controls if the timer will be shown in the middle of the screen", null, new ConfigurationManagerAttributes { Order = order }));
            mTimerScreen = (ConfigEntry<bool>)Config[TimerScreenDef];
            order--;
            

            Config.SettingChanged += onSettingChanged;
            onSettingChanged(null, null);

            Harmony harmony = new Harmony(pluginGuid);
            harmony.PatchAll(Assembly.GetExecutingAssembly());

            isCheat = false;
            isEnabled = mEnabled.Value;

            PolyTechMain.registerMod(this);
        }

        public void onEnableDisable(object sender, EventArgs e)
        {
            isEnabled = mEnabled.Value;
        }

        public void onSettingChanged(object sender, EventArgs e)
        {
            if (!mTimerScreen.Value && GameUI.m_Instance != null) GameUI.m_Instance.m_Status.Close();
        }


        public override void enableMod()
        {
            this.isEnabled = true;
            mEnabled.Value = true;
            onEnableDisable(null, null);
        }

        public override void disableMod()
        {
            this.isEnabled = false;
            mEnabled.Value = false;
            onEnableDisable(null, null);
        }

        public override string getSettings()
        {
            return "";
        }

        public override void setSettings(string st)
        {
            return;
        }

        private static bool CheckForCheating()
        {
            return mEnabled.Value && PolyTechMain.modEnabled.Value;
        }


        //main
        [HarmonyPatch(typeof(Main), "Update")]
        private static class patchUpdate
        {
            private static void Postfix()
            {
                if (!CheckForCheating()) return;

                InUpdate = true;
                Key = PolyTwitch.m_Key;


                //auto mute
                if (mAutoMute.Value.IsDown() && !AutoMuteDown && !Banning)
                {
                    PopUpMessage.Display("Mute all players who failed?", new Panel_PopUpMessage.OnChoiceDelegate(OnAutoMute));
                }
                else if (mAutoMute.Value.IsDown() && !AutoMuteDown && Banning)
                {
                    GameUI.m_Instance.m_Status.Open("canceling operation");
                    currentAction.completed -= OnBanComplete;
                    currentAction.completed += OnBanStopped;
                }
                AutoMuteDown = mAutoMute.Value.IsDown();


                //whitelist
                if (mCreateWhitelist.Value.IsDown() && !CreateWhitelistDown)
                {
                    if(!mSecondOpp.Value.IsPressed()) PopUpTwoChoices.Display("Disable/Enable whitelist?\nWhitelist is " + (whitelistEnabled ? "enabled" : "disabled"), "enable", "disable", new Panel_PopUpTwoChoices.OnChoiceDelegate(OnEnableWhitelist), new Panel_PopUpTwoChoices.OnChoiceDelegate(OnDisableWhitelist));
                    else PopUpMessage.Display("Overwrite whitelist?", delegate { PopUpTwoChoices.Display("Overwrite whitelist with", "Current Players", "Empty", new Panel_PopUpTwoChoices.OnChoiceDelegate(OnCreateWhitelistAuto), new Panel_PopUpTwoChoices.OnChoiceDelegate(OnCreateWhitelistEmpty)); });
                }
                CreateWhitelistDown = mCreateWhitelist.Value.IsDown();

                if (mAddWhitelist.Value.IsDown() && !AddWhitelistDown)
                {
                    string Names = "";
                    bool First = true;
                    foreach(string Name in whitelist)
                    {
                        if (!First) Names += ", ";
                        Names += Name;
                        First = false;
                    }

                    if(!mSecondOpp.Value.IsPressed()) PopupInputField.Display("Add to whitelist?", Names, new Panel_PopUpInputField.OnOkDelegate(OnAddToWhitelist));
                    else PopupInputField.Display("Remove from whitelist?", Names, new Panel_PopUpInputField.OnOkDelegate(OnRemoveFromWhitelist));
                }
                AddWhitelistDown = mAddWhitelist.Value.IsDown();

                if (whitelistEnabled) TopLeftMessage("Whitelist is enabled: " + whitelist.Count + " players", 3);

                
                //timer
                if (mSetTimer.Value.IsDown() && !SetTimerDown)
                {
                    if (!mSecondOpp.Value.IsPressed())
                    {
                        if (!TimerEnabled)
                        {
                            PopupInputField.Display("Start timer?", "MM;SS", new Panel_PopUpInputField.OnOkDelegate(OnSetTimer));
                        }
                        else
                        {
                            PopupInputField.Display("Add to timer?", "MM;SS", new Panel_PopUpInputField.OnOkDelegate(OnSetTimer));
                        }
                    }
                    else if (TimerEnabled) PopUpMessage.Display("Stop timer?", new Panel_PopUpMessage.OnChoiceDelegate(OnStopTimer));
                    else PopUpWarning.Display("Timer is not active!");
                }
                SetTimerDown = mSetTimer.Value.IsDown();
                if (TimerEnabled) TimerUpdate();

                InUpdate = false;
            }
        }
        

        //timer
        public static void OnSetTimer(string text)
        {
            int Minutes = 0;
            string[] TextArr = text.Split(';');
            if (TextArr.Length != 1 && TextArr.Length != 2)
            {
                PopUpWarning.Display(text + " is not in the correct format!");
                return;
            }
            if (!int.TryParse(TextArr[TextArr.Length - 1], out int Seconds))
            {
                PopUpWarning.Display("Could not parse " + TextArr[TextArr.Length - 1] + " to a number!");
                return;
            }
            if (TextArr.Length == 2)
            {
                if (!int.TryParse(TextArr[0], out Minutes))
                {
                    PopUpWarning.Display("Could not parse " + TextArr[0] + " to a number!");
                    return;
                }
            }
            Seconds += Minutes * 60;
            TimerSeconds += Seconds;
            if (!TimerEnabled)
            {
                TimerSeconds = Seconds + 1;
                TimerEnabled = true;
                BeginingTimer = DateTime.Now;
                Profile.m_TwitchAllowSuggestions = true;
                LastSuggestion = "-";
            }
        }

        public static void TimerUpdate()
        {
            DateTime EndTimer = BeginingTimer.AddSeconds(TimerSeconds);
            TimeSpan time = EndTimer - DateTime.Now;
            TopCenterMessage((int)time.TotalMinutes + ":" + time.ToString(@"ss"), 2);
            if (mTimerScreen.Value) GameUI.m_Instance.m_Status.Open((int)time.TotalMinutes + ":" + time.ToString(@"ss") + "\nLatest suggestion from: " + LastSuggestion);

            if (time.TotalSeconds <= 0)
            {
                OnStopTimer();
            }
        }

        public static void OnStopTimer()
        {
            GameUI.m_Instance.m_Status.Close();
            TimerEnabled = false;
            Profile.m_TwitchAllowSuggestions = false;
            TopCenterMessage("Timer has stopped!", 3);
            PopUpWarning.Display("Out of time!\nSuggestions are now closed!");
        }


        //whitelist
        public static void OnCreateWhitelistAuto()
        {
            whitelist.Clear();
            whitelistEnabled = true;
            foreach (PolyTwitchSuggestion suggestion in PolyTwitchSuggestions.m_Suggestions)
            {
                if (!whitelist.Contains(suggestion.m_Username) && !suggestion.m_Muted) whitelist.Add(suggestion.m_Username);
            }
            string message = "Whitelist created and enabled!\n";
            if (whitelist.Count != 1) message += whitelist.Count + " players!";
            else message += whitelist.Count + " player!";
            PopUpWarning.Display(message);
        }

        public static void OnCreateWhitelistEmpty()
        {
            whitelist.Clear();
            whitelistEnabled = true;
            PopUpWarning.Display("Whitelist created and enabled!");
        }

        public static void OnAddToWhitelist(string names)
        {
            string[] Names = names.Split(new string[] {", "}, StringSplitOptions.RemoveEmptyEntries);

            int Succes = 0;
            foreach (string str in Names)
            {
                if (!whitelist.Contains(str)) whitelist.Add(str);
                else continue;
                Succes++;
            }
            if (Succes != 1) PopUpWarning.Display(Succes + " players added to the whitelist");
            else PopUpWarning.Display(Succes + " player added to the whitelist");
        }

        public static void OnRemoveFromWhitelist(string names)
        {
            string[] Names = names.Split(new string[] { ", " }, StringSplitOptions.RemoveEmptyEntries);

            int Succes = 0;
            foreach (string str in Names)
            {
                if (whitelist.Contains(str)) whitelist.Remove(str);
                else continue;
                Succes++;
            }
            if (Succes != 1) PopUpWarning.Display(Succes + " players removed from the whitelist");
            else PopUpWarning.Display(Succes + " player removed from the whitelist");
        }

        public static void OnEnableWhitelist()
        {
            whitelistEnabled = true;
        }

        public static void OnDisableWhitelist()
        {
            whitelistEnabled = false;
            TopLeftMessage("Whitelist disabled!", 3);
        }

        [HarmonyPatch(typeof(PolyTwitchSuggestions), "Create")]
        private static class patchSuggestions
        {
            private static bool Prefix(string username)
            {
                if (!CheckForCheating()) return true;
                LastSuggestion = username;
                if (whitelistEnabled && !whitelist.Contains(username)) return false;
                return true;
            }
        }


        //auto mute
        public static void OnAutoMute()
        {
            Banning = true;
            Failed = new List<PolyTwitchSuggestion>();
            List<string> IDs = new List<string>();
            foreach (PolyTwitchSuggestion suggestion in PolyTwitchSuggestions.m_Suggestions)
            {
                int cost = CalculateBridgeCost(suggestion);
                int budget = Budget.m_CashBudget;
                bool BudgetCheat = budget >= Budget.UNLIMITED_CASH_BUDGET || Budget.m_UsingForcedUnlimitedBudget;
                if ((suggestion.m_Status != PolyTwitchSuggestionStatus.PASSED || (!BudgetCheat && cost > budget)) && !suggestion.m_Muted && !IDs.Contains(suggestion.m_OwnerId))
                {
                    Failed.Add(suggestion);
                    IDs.Add(suggestion.m_OwnerId);
                }
            }
            MaxBans = Failed.Count;

            if (Failed.Count > 0)
            {
                BanAmount = 1;
                Debug.Log("Muting " + Failed[0].m_Username + " (" + BanAmount + "/" + MaxBans + ")");
                GameUI.m_Instance.m_Status.Open(GetMuteMessage(Failed[0].m_Username) + "\n" + BanAmount + "/" + MaxBans);
                currentAction = WebRequest.Post("https://api.t2.drycactus.com/v1/" + string.Format("streamer/stream/user/{0}/ban", Failed[0].m_OwnerId), PolyTwitch.m_Key).SendWebRequest();
                currentAction.completed += OnBanComplete;
                Failed.RemoveAt(0);
            }
            else
            {
                PopUpWarning.Display("No one was muted!");
                GameUI.m_Instance.m_Status.Close();
                Banning = false;
            }
        }

        public static void OnBanComplete(UnityEngine.AsyncOperation asyncOperation)
        {
            Debug.Log("Completed");
            Banning = false;
            UnityWebRequestAsyncOperation unityWebRequestAsyncOperation = (UnityWebRequestAsyncOperation)asyncOperation;
            Debug.Log(unityWebRequestAsyncOperation.webRequest.downloadHandler.text);
            if (unityWebRequestAsyncOperation.webRequest.isNetworkError || unityWebRequestAsyncOperation.webRequest.isHttpError)
            {
                string errorMessage = WebRequest.GetErrorMessage(unityWebRequestAsyncOperation.webRequest);
                Debug.LogWarning("failed: " + errorMessage);
                GameUI.m_Instance.m_Status.Close();
                PopUpWarning.Display("something went wrong, please try again.\nmuted "+ (BanAmount - 1) + "/" + MaxBans);
            }
            else
            {
                if (Failed.Count > 0)
                {
                    Banning = true;
                    BanAmount++;
                    Debug.Log("Muting " + Failed[0].m_Username + " (" + BanAmount + "/" + MaxBans + ")");
                    GameUI.m_Instance.m_Status.Open(GetMuteMessage(Failed[0].m_Username) + "\n" + BanAmount + "/" + MaxBans);
                    currentAction = WebRequest.Post("https://api.t2.drycactus.com/v1/" + string.Format("streamer/stream/user/{0}/ban", Failed[0].m_OwnerId), PolyTwitch.m_Key).SendWebRequest();
                    currentAction.completed += OnBanComplete;
                    Failed.RemoveAt(0);
                }
                else
                {
                    if(BanAmount != 1) PopUpWarning.Display("Muted " + BanAmount + " players!");
                    else PopUpWarning.Display("Muted " + BanAmount + " player!");
                    GameUI.m_Instance.m_Status.Close();
                }
            }
        }

        public static void OnBanStopped(UnityEngine.AsyncOperation asyncOperation)
        {
            Debug.Log("Completed");
            Banning = false;
            UnityWebRequestAsyncOperation unityWebRequestAsyncOperation = (UnityWebRequestAsyncOperation)asyncOperation;
            Debug.Log(unityWebRequestAsyncOperation.webRequest.downloadHandler.text);
            if (unityWebRequestAsyncOperation.webRequest.isNetworkError || unityWebRequestAsyncOperation.webRequest.isHttpError)
            {
                string errorMessage = WebRequest.GetErrorMessage(unityWebRequestAsyncOperation.webRequest);
                Debug.LogWarning("failed: " + errorMessage);
                GameUI.m_Instance.m_Status.Close();
                PopUpWarning.Display("something went wrong, please try again.\nmuted " + (BanAmount - 1) + "/" + MaxBans);
            }
            else
            {
                if (MaxBans != 1) PopUpWarning.Display("Muted " + BanAmount + "/" + MaxBans + " players!");
                else PopUpWarning.Display("Muting canceled\nMuted " + BanAmount + "/" + MaxBans + " player!");
                GameUI.m_Instance.m_Status.Close();
            }
        }


        //KeyBinds
        [HarmonyPatch(typeof(KeyboardShortcut), "ModifierKeyTest")]
        private static class patchKeyBinds
        {
            private static void Postfix(ref bool __result, KeyboardShortcut __instance)
            {
                if (!CheckForCheating() || !InUpdate) return;
                KeyCode mainKey = __instance.MainKey;
                __result = __instance.Modifiers.All((KeyCode c) => c == mainKey || Input.GetKey(c));
            }
        }

        //Whitelist
        [HarmonyPatch(typeof(Panel_PopUpInputField), "OnEnable")]
        private static class inputField
        {
            private static void Postfix(Panel_PopUpInputField __instance)
            {
                if (!CheckForCheating() || !InUpdate) return;

                __instance.m_InputField.characterLimit = int.MaxValue;
            }
        }
        
        [HarmonyPatch(typeof(PolyTwitchBans), "MutePlayer")]
        private static class onMute
        {
            private static void Prefix(ref string username)
            {
                if (!CheckForCheating() || !mWhitelistMute.Value) return;

                if (whitelist.Contains(username)) whitelist.Remove(username);
            }
        }

        //webmessage template
        private static void OnSomethingComplete(UnityEngine.AsyncOperation asyncOperation)
        {
            Debug.Log("Completed");
            UnityWebRequestAsyncOperation unityWebRequestAsyncOperation = (UnityWebRequestAsyncOperation)asyncOperation;
            Debug.Log(unityWebRequestAsyncOperation.webRequest.downloadHandler.text);
            if (unityWebRequestAsyncOperation.webRequest.isNetworkError || unityWebRequestAsyncOperation.webRequest.isHttpError)
            {
                string errorMessage = WebRequest.GetErrorMessage(unityWebRequestAsyncOperation.webRequest);
                Debug.LogWarning("failed: " + errorMessage);
            }
            else
            {

            }
        }


        //stuff
        public static int CalculateBridgeCost(PolyTwitchSuggestion suggestion)
        {
            BridgePreviewMaker.GeneratePreview(suggestion.m_BridgeSaveData, GameUI.m_Instance.m_PolyTwitchBridge.m_BuildPreviewImage);
            return Mathf.RoundToInt(BridgePreviewMaker.m_BridgeCost);
        }

        public static void TopLeftMessage(string message, float time)
        {
            GameUI.m_Instance.m_TopBar.m_MessageTopLeft.ShowMessage(message, time);
        }

        public static void TopCenterMessage(string message, float time)
        {
            GameUI.m_Instance.m_TopBar.m_MessageTopCenter.ShowMessage(message, time);
        }



    public static string GetMuteMessage(string name)
    {
        switch (name.ToLower())
	    {
            case "bolt_986": return "Haha, " + name + " go brrrrrrrrr";
		    default: return "Muting " + name;
	    }
    }
}




    /// <summary>
    /// Class that specifies how a setting should be displayed inside the ConfigurationManager settings window.
    /// 
    /// Usage:
    /// This class template has to be copied inside the plugin's project and referenced by its code directly.
    /// make a new instance, assign any fields that you want to override, and pass it as a tag for your setting.
    /// 
    /// If a field is null (default), it will be ignored and won't change how the setting is displayed.
    /// If a field is non-null (you assigned a value to it), it will override default behavior.
    /// </summary>
    /// 
    /// <example> 
    /// Here's an example of overriding order of settings and marking one of the settings as advanced:
    /// <code>
    /// // Override IsAdvanced and Order
    /// Config.AddSetting("X", "1", 1, new ConfigDescription("", null, new ConfigurationManagerAttributes { IsAdvanced = true, Order = 3 }));
    /// // Override only Order, IsAdvanced stays as the default value assigned by ConfigManager
    /// Config.AddSetting("X", "2", 2, new ConfigDescription("", null, new ConfigurationManagerAttributes { Order = 1 }));
    /// Config.AddSetting("X", "3", 3, new ConfigDescription("", null, new ConfigurationManagerAttributes { Order = 2 }));
    /// </code>
    /// </example>
    /// 
    /// <remarks> 
    /// You can read more and see examples in the readme at https://github.com/BepInEx/BepInEx.ConfigurationManager
    /// You can optionally remove fields that you won't use from this class, it's the same as leaving them null.
    /// </remarks>
#pragma warning disable 0169, 0414, 0649
    internal sealed class ConfigurationManagerAttributes
    {
        /// <summary>
        /// Should the setting be shown as a percentage (only use with value range settings).
        /// </summary>
        public bool? ShowRangeAsPercent;

        /// <summary>
        /// Custom setting editor (OnGUI code that replaces the default editor provided by ConfigurationManager).
        /// See below for a deeper explanation. Using a custom drawer will cause many of the other fields to do nothing.
        /// </summary>
        public System.Action<BepInEx.Configuration.ConfigEntryBase> CustomDrawer;

        /// <summary>
        /// Show this setting in the settings screen at all? If false, don't show.
        /// </summary>
        public bool? Browsable;

        /// <summary>
        /// Category the setting is under. Null to be directly under the plugin.
        /// </summary>
        public string Category;

        /// <summary>
        /// If set, a "Default" button will be shown next to the setting to allow resetting to default.
        /// </summary>
        public object DefaultValue;

        /// <summary>
        /// Force the "Reset" button to not be displayed, even if a valid DefaultValue is available. 
        /// </summary>
        public bool? HideDefaultButton;

        /// <summary>
        /// Force the setting name to not be displayed. Should only be used with a <see cref="CustomDrawer"/> to get more space.
        /// Can be used together with <see cref="HideDefaultButton"/> to gain even more space.
        /// </summary>
        public bool? HideSettingName;

        /// <summary>
        /// Optional description shown when hovering over the setting.
        /// Not recommended, provide the description when creating the setting instead.
        /// </summary>
        public string Description;

        /// <summary>
        /// Name of the setting.
        /// </summary>
        public string DispName;

        /// <summary>
        /// Order of the setting on the settings list relative to other settings in a category.
        /// 0 by default, higher number is higher on the list.
        /// </summary>
        public int? Order;

        /// <summary>
        /// Only show the value, don't allow editing it.
        /// </summary>
        public bool? ReadOnly;

        /// <summary>
        /// If true, don't show the setting by default. User has to turn on showing advanced settings or search for it.
        /// </summary>
        public bool? IsAdvanced;

        /// <summary>
        /// Custom converter from setting type to string for the built-in editor textboxes.
        /// </summary>
        public System.Func<object, string> ObjToStr;

        /// <summary>
        /// Custom converter from string to setting type for the built-in editor textboxes.
        /// </summary>
        public System.Func<string, object> StrToObj;
    }
}