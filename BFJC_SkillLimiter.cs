/* BFJC_SkillLimiter.cs

by PapaCharlie9@gmail.com

Free to use as is in any way you want with no warranty.

*/

using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using System.Collections;
using System.Net;
using System.Web;
using System.Data;
using System.Threading;
using System.Timers;
using System.Diagnostics;
using System.Reflection;

using PRoCon.Core;
using PRoCon.Core.Plugin;
using PRoCon.Core.Plugin.Commands;
using PRoCon.Core.Players;
using PRoCon.Core.Players.Items;
using PRoCon.Core.Battlemap;
using PRoCon.Core.Maps;


namespace PRoConEvents
{

//Aliases
using EventType = PRoCon.Core.Events.EventType;
using CapturableEvent = PRoCon.Core.Events.CapturableEvents;

public class BFJC_SkillLimiter : PRoConPluginAPI, IPRoConPluginInterface
{

/* Inherited:
    this.PunkbusterPlayerInfoList = new Dictionary<string, CPunkbusterInfo>();
    this.FrostbitePlayerInfoList = new Dictionary<string, CPlayerInfo>();
*/

private bool fIsEnabled;
private int fDebugLevel;

private HashSet<String> kickTargetPlayer;
private int serverPlayerCount = 0;
private int serverMaxPlayerCount = int.MaxValue;
private bool isShowedAverageSkill = false;
private string roundAverageMessage = null;
private int currentRspmLimit = 0;
private int currentSkillLimit = 0;
private string currentGamemode = "ConquestLarge0";

/// <summary>
/// スタッツフェッチスレッド
/// </summary>
private Thread statsFetchThread;

/// <summary>
/// スタッツフェッチスレッドキャンセルフラグ
/// </summary>
private bool isCancelStatsFetchThread = false;

/// <summary>
/// スタッツ取得キュー
/// </summary>
private Queue<string> playerJoinQueue = new Queue<string>();

private int limitEnabledPlayerNumSetting = 50;
private int rspmLimitSetting = 0;
private int maxRspmLimitSetting = int.MaxValue;
private int dynamicRspmLimitSetting = 50;
private int skillLimitSetting = 0;
private int maxSkillLimitSetting = int.MaxValue;
private int dynamicSkillLimitSetting = 50;

private string limiterOffServerNameSetting = "i3D.net [JPN] BFJC 01 NVIDIA x TGN (Cq)";
private string limiterOnServerNameSetting = "i3D.net [JPN] BFJC 01 NVIDIA x TGN (Cq)";
private string kickMessageSetting = "SORRY!! Your Skill level is under limit. This server is for Expert.";

//private const string BF4STATS_PLAYERINFO_URL_BASE_JSON = "http://api.bf4stats.com/api/playerInfo?plat=pc&output=json&opt=stats&opt=extra&name={0}";
private const int WEB_RETRY_COUNT = 3;
private const int WEB_RETRY_DELAY = 1000;
private const int GETSTATS_RETRY_COUNT = 3;
private const int GETSTATS_RETRY_DELAY = 4000;
private const int STATSNOW_RSPM_API_GET_NUMROUNDS = 25;

private static Dictionary<string, string> JSON_REQUEST_HEADERS = null;

public BFJC_SkillLimiter() {
	fIsEnabled = false;
	fDebugLevel = 2;

    // JSONリクエストヘッダ(使いまわすのでここで初期化)
    JSON_REQUEST_HEADERS = new Dictionary<string, string>();
    JSON_REQUEST_HEADERS.Add("Content-Type", "application/json");
}

public enum MessageType { Warning, Error, Exception, Normal };

public String FormatMessage(String msg, MessageType type) {
    String prefix = "[^bBFJC_SkillLimiter^n] ";

	if (type.Equals(MessageType.Warning))
		prefix += "^1^bWARNING^0^n: ";
	else if (type.Equals(MessageType.Error))
		prefix += "^1^bERROR^0^n: ";
	else if (type.Equals(MessageType.Exception))
		prefix += "^1^bEXCEPTION^0^n: ";

	return prefix + msg;
}


public void LogWrite(String msg)
{
	this.ExecuteCommand("procon.protected.pluginconsole.write", msg);
}

public void ConsoleWrite(string msg, MessageType type)
{
	LogWrite(FormatMessage(msg, type));
}

public void ConsoleWrite(string msg)
{
	ConsoleWrite(msg, MessageType.Normal);
}

public void ConsoleWarn(String msg)
{
	ConsoleWrite(msg, MessageType.Warning);
}

public void ConsoleError(String msg)
{
	ConsoleWrite(msg, MessageType.Error);
}

public void ConsoleException(String msg)
{
	ConsoleWrite(msg, MessageType.Exception);
}

public void DebugWrite(string msg, int level)
{
	if (fDebugLevel >= level) ConsoleWrite(msg, MessageType.Normal);
}


public void ServerCommand(params String[] args)
{
	List<string> list = new List<string>();
	list.Add("procon.protected.send");
	list.AddRange(args);
	this.ExecuteCommand(list.ToArray());
}


public string GetPluginName() {
    return "BFJC-SkillLimiter";
}

public string GetPluginVersion() {
	return "0.0.8";
}

public string GetPluginAuthor() {
	return "Aogik";
}

public string GetPluginWebsite() {
    return "bf.jpcommunity.com/";
}

public string GetPluginDescription() {
	return @"
<h2>Description</h2>
<p>This Plugin limit player skill level. (Beta Version)</p>

<h2>Settings</h2>
<p>beta version</p>

<h2>Development</h2>
<p>Battlefield JP Community</p>

<h3>Changelog</h3>
<blockquote><h4>0.0.8 (2014/09/22)</h4>
	- Support StatsNow API.<br/>
</blockquote>

<blockquote><h4>0.0.7 (2014/08/30)</h4>
	- Add Max Limit Setting.<br/>
</blockquote>

<blockquote><h4>0.0.6 (2014/08/09)</h4>
	- Add RSPM Limit.<br/>
</blockquote>

<blockquote><h4>0.0.5 (2014/04/20)</h4>
	- Fixed bugs.<br/>
</blockquote>

<blockquote><h4>0.0.4 (2014/04/20)</h4>
	- Add dynamic average skill limit.<br/>
</blockquote>

<blockquote><h4>0.0.3 (2014/04/13)</h4>
	- Add display average skill function.<br/>
</blockquote>

<blockquote><h4>0.0.2 (2014/04/13)</h4>
	- Retry getting stats, if retrieve data error.<br/>
</blockquote>

<blockquote><h4>0.0.1 (2014/04/12)</h4>
	- initial version<br/>
</blockquote>
";
}




public List<CPluginVariable> GetDisplayPluginVariables() {

	List<CPluginVariable> lstReturn = new List<CPluginVariable>();

	lstReturn.Add(new CPluginVariable("BFJC-SkillLimiter|Debug level", fDebugLevel.GetType(), fDebugLevel));

    lstReturn.Add(new CPluginVariable("Skill Limiter Settings|Limiter Enabled PlayerNum", limitEnabledPlayerNumSetting.GetType(), limitEnabledPlayerNumSetting));
    lstReturn.Add(new CPluginVariable("Skill Limiter Settings|Limiter Default RSPM Level", rspmLimitSetting.GetType(), rspmLimitSetting));
    lstReturn.Add(new CPluginVariable("Skill Limiter Settings|Limiter Max RSPM Level", maxRspmLimitSetting.GetType(), maxRspmLimitSetting));
    lstReturn.Add(new CPluginVariable("Skill Limiter Settings|Limiter Dynamic RSPM Limit Percent", dynamicRspmLimitSetting.GetType(), dynamicRspmLimitSetting));
    lstReturn.Add(new CPluginVariable("Skill Limiter Settings|Limiter Default Skill Level", skillLimitSetting.GetType(), skillLimitSetting));
    lstReturn.Add(new CPluginVariable("Skill Limiter Settings|Limiter Max Skill Level", maxSkillLimitSetting.GetType(), maxSkillLimitSetting));
    lstReturn.Add(new CPluginVariable("Skill Limiter Settings|Limiter Dynamic Skill Limit Percent", dynamicSkillLimitSetting.GetType(), dynamicSkillLimitSetting));
    lstReturn.Add(new CPluginVariable("Skill Limiter Settings|Limiter OFF Server Name", limiterOffServerNameSetting.GetType(), limiterOffServerNameSetting));
    lstReturn.Add(new CPluginVariable("Skill Limiter Settings|Limiter ON Server Name", limiterOnServerNameSetting.GetType(), limiterOnServerNameSetting));
    lstReturn.Add(new CPluginVariable("Skill Limiter Settings|Kick Message", kickMessageSetting.GetType(), kickMessageSetting));

	return lstReturn;
}

public List<CPluginVariable> GetPluginVariables() {
	return GetDisplayPluginVariables();
}

public void SetPluginVariable(string strVariable, string strValue) {
	if (Regex.Match(strVariable, @"Debug level").Success) {
		int tmp = 2;
		int.TryParse(strValue, out tmp);
		fDebugLevel = tmp;
	}

    if (Regex.Match(strVariable, @"Limiter Enabled PlayerNum").Success)
    {
        int tmp = 0;
        int.TryParse(strValue, out tmp);
        limitEnabledPlayerNumSetting = tmp;

        ConsoleWrite("(Settings) Limiter Enabled PlayerNum = " + limitEnabledPlayerNumSetting);
    }

    if (Regex.Match(strVariable, @"Limiter Default RSPM Level").Success)
    {
        int tmp = 0;
        int.TryParse(strValue, out tmp);
        rspmLimitSetting = tmp;

        ConsoleWrite("(Settings) Limiter Default RSPM Level = " + rspmLimitSetting);
    }

    if (Regex.Match(strVariable, @"Limiter Max RSPM Level").Success)
    {
        int tmp = 0;
        int.TryParse(strValue, out tmp);
        maxRspmLimitSetting = tmp;

        ConsoleWrite("(Settings) Limiter Max RSPM Level = " + maxRspmLimitSetting);
    }

    if (Regex.Match(strVariable, @"Limiter Dynamic RSPM Limit Percent").Success)
    {
        int tmp = 50;
        int.TryParse(strValue, out tmp);
        dynamicRspmLimitSetting = tmp;

        ConsoleWrite("(Settings) Limiter Dynamic RSPM Limit Percent = " + dynamicRspmLimitSetting);
    }

    if (Regex.Match(strVariable, @"Limiter Default Skill Level").Success)
    {
        int tmp = 0;
        int.TryParse(strValue, out tmp);
        skillLimitSetting = tmp;

        ConsoleWrite("(Settings) Limiter Default Skill Level = " + skillLimitSetting);
    }

    if (Regex.Match(strVariable, @"Limiter Max Skill Level").Success)
    {
        int tmp = 0;
        int.TryParse(strValue, out tmp);
        maxSkillLimitSetting = tmp;

        ConsoleWrite("(Settings) Limiter Max Skill Level = " + maxSkillLimitSetting);
    }

    if (Regex.Match(strVariable, @"Limiter Dynamic Skill Limit Percent").Success)
    {
        int tmp = 50;
        int.TryParse(strValue, out tmp);
        dynamicSkillLimitSetting = tmp;

        ConsoleWrite("(Settings) Limiter Dynamic Skill Limit Percent = " + dynamicSkillLimitSetting);
    }

    if (Regex.Match(strVariable, @"Limiter OFF Server Name").Success)
    {
        limiterOffServerNameSetting = strValue;

        ConsoleWrite("(Settings) Limiter OFF Server Name = " + limiterOffServerNameSetting);
    }

    if (Regex.Match(strVariable, @"Limiter ON Server Name").Success)
    {
        limiterOnServerNameSetting = strValue;

        ConsoleWrite("(Settings) Limiter ON Server Name = " + limiterOnServerNameSetting);
    }

    if (Regex.Match(strVariable, @"Kick Message").Success)
    {
        kickMessageSetting = strValue;

        ConsoleWrite("(Settings) Kick Message = " + kickMessageSetting);
    }
}


public void OnPluginLoaded(string strHostName, string strPort, string strPRoConVersion) {
	this.RegisterEvents(this.GetType().Name, "OnVersion", "OnServerInfo", "OnResponseError", "OnListPlayers", "OnPlayerJoin", "OnPlayerLeft", "OnPlayerKilled", "OnPlayerSpawned", "OnPlayerTeamChange", "OnGlobalChat", "OnTeamChat", "OnSquadChat", "OnRoundOverPlayers", "OnRoundOver", "OnRoundOverTeamScores", "OnLoadingLevel", "OnLevelStarted", "OnLevelLoaded");
}

public void OnPluginEnable() {
	fIsEnabled = true;
    currentRspmLimit = this.rspmLimitSetting;
    currentSkillLimit = this.skillLimitSetting;
    kickTargetPlayer = new HashSet<string>();
    playerJoinQueue = new Queue<string>();
    isCancelStatsFetchThread = false;
    statsFetchThread = new Thread(new ThreadStart(fetchPlayerStats));
    statsFetchThread.Start();
	ConsoleWrite("Enabled!");
}

public void OnPluginDisable() {
	fIsEnabled = false;
    kickTargetPlayer = null;
    if (this.statsFetchThread != null)
    {
        // スレッドキャンセルリクエスト
        this.isCancelStatsFetchThread = true;
        // スレッドが終了するまで待つ
        this.statsFetchThread.Join(5000);
    }
    playerJoinQueue = null;
	ConsoleWrite("Disabled!");
}


public override void OnVersion(string serverType, string version) { }

public override void OnServerInfo(CServerInfo serverInfo) {
    // サーバ上のプレイヤー数を保持
    this.serverPlayerCount = serverInfo.PlayerCount;
    // サーバの最大人数を保持（コマンダーを除いた数）
    this.serverMaxPlayerCount = serverInfo.MaxPlayerCount;

    /*
     * サーバ名の変更 
     */
    if (this.serverPlayerCount < this.limitEnabledPlayerNumSetting)
    {
        if (serverInfo.ServerName != this.limiterOffServerNameSetting)
        {
            // リミッターOFF時のサーバ名に変更
            setServerName(this.limiterOffServerNameSetting);
        }

        // ゴミが残っている可能性があるのでクリアする
        if (kickTargetPlayer.Count != 0) { kickTargetPlayer.Clear(); }
    }
    else
    {
        // リミッターON時のサーバ名
        //string newServerName = String.Format(this.limiterOnServerNameSetting, this.currentSkillLimit);
        string newServerName = String.Format(this.limiterOnServerNameSetting, this.currentRspmLimit);

        if (serverInfo.ServerName != newServerName)
        {
            // リミッターON時のサーバ名に変更
            setServerName(newServerName);
        }
    }
}

public override void OnResponseError(List<string> requestWords, string error) { }

public override void OnListPlayers(List<CPlayerInfo> players, CPlayerSubset subset)
{
    if (!this.isShowedAverageSkill && players.Count >= this.serverMaxPlayerCount)
    {
        // 平均スキル表示済みフラグ
        this.isShowedAverageSkill = true;

        // 平均スキル計算スレッド起動
        new Thread(new ThreadStart(() =>
        {
            ConsoleWrite("(CalcThread) Start calculating average RSPM/SKILL !! PlayerCount: " + players.Count);

            int sumRspm = 0;
            int sumSkill = 0;
            int availableRspmCnt = 0;
            int availableStatsCnt = 0;
            foreach (CPlayerInfo player in players)
            {
                int? playerRspm = getPlayerRspmApi(player.SoldierName, this.currentGamemode, STATSNOW_RSPM_API_GET_NUMROUNDS);
                if (playerRspm != null)
                {
                    // RSPM値の合計
                    sumRspm += playerRspm.Value;

                    // RSPM値を取得出来た人数
                    availableRspmCnt++;

                    ConsoleWrite("(CalcThread) [" + player.SoldierName + "] RSPM : " + playerRspm.Value + " Sum: " + sumRspm + " Cnt: " + availableRspmCnt);
                }

                Thread.Sleep(1500);

                PlayerStats playerStats = getPlayerStats(player.SoldierName);
                if (playerStats != null)
                {
                    // スキル値の合計
                    sumSkill += playerStats.Skill;

                    // スキル値を取得出来た人数
                    availableStatsCnt++;

                    ConsoleWrite("(CalcThread) [" + player.SoldierName + "] Skill: " + playerStats.Skill + " Sum: " + sumSkill + " Cnt: " + availableStatsCnt);
                }

                Thread.Sleep(1500); // 3秒に一人ずつ
            }

            if (availableRspmCnt > 0)
            {
                // RSPM平均値
                int average = sumRspm / availableRspmCnt;

                // 現在のRSPMリミット値を更新
                this.currentRspmLimit = average * this.dynamicRspmLimitSetting / 100; // 平均RSPMのN%
                if (this.currentRspmLimit > this.maxRspmLimitSetting)
                {
                    // RSPMの最大値
                    this.currentRspmLimit = this.maxRspmLimitSetting;
                }

                StringBuilder message = new StringBuilder();
                message.Append("The round average RSPM: ");
                message.Append(average);
                message.Append(" !");
                if (average >= 350) { message.Append("!"); }
                if (average >= 400) { message.Append("!"); }
                if (average >= 500) { message.Append("!"); }
                ConsoleWrite("(CalcThread) " + message.ToString());

                //// ADMINメッセージ
                //SendGlobalMessage(message.ToString());
                // PROCONチャットメッセージ
                ProconChatMessage(message.ToString());

                // ラウンドの平均値メッセージ保持
                this.roundAverageMessage = message.ToString();
            }

            if (availableStatsCnt > 0)
            {
                // スキル平均値
                int average = sumSkill / availableStatsCnt;

                // 現在のスキルリミット値を更新
                this.currentSkillLimit = average * this.dynamicSkillLimitSetting / 100; // 平均スキルのN%
                if (this.currentSkillLimit > this.maxSkillLimitSetting)
                {
                    // スキルの最大値
                    this.currentSkillLimit = this.maxSkillLimitSetting;
                }

                StringBuilder message = new StringBuilder();
                message.Append("The round average skill: ");
                message.Append(average);
                message.Append(" !");
                if (average >= 230) { message.Append("!"); }
                if (average >= 270) { message.Append("!"); }
                if (average >= 300) { message.Append("!"); }
                ConsoleWrite("(CalcThread) " + message.ToString());

                //// ADMINメッセージ
                //SendGlobalMessage(message.ToString());
                // PROCONチャットメッセージ
                ProconChatMessage(message.ToString());
            }
        })).Start();
    }
}

public override void OnPlayerJoin(string soldierName)
{
    if (this.serverPlayerCount >= this.limitEnabledPlayerNumSetting)
    {
        // プレイヤースタッツ取得キューに追加
        this.playerJoinQueue.Enqueue(soldierName);
    }
}

/// <summary>
/// プレイヤーのスタッツを取得するスレッド
/// </summary>
private void fetchPlayerStats()
{
    while (!this.isCancelStatsFetchThread)
    {
        if (this.playerJoinQueue.Count > 0)
        {
            ConsoleWrite("^2^bQUEUE^n " + this.playerJoinQueue.Count + " still in queue. NowRspmLimit: " + this.currentRspmLimit + " / NowSkillLimit: " + this.currentSkillLimit);

            // キューからプレイヤーを取得
            string soldierName = this.playerJoinQueue.Dequeue();

            ConsoleWrite("Getting player STATS for [" + soldierName + "]");

            int? playerRspm = getPlayerRspmApi(soldierName, this.currentGamemode, STATSNOW_RSPM_API_GET_NUMROUNDS);
            if (playerRspm != null)
            {
                // RSPM取得成功
                ConsoleWrite("[" + soldierName + "] RSPM : " + playerRspm.Value);

                if (playerRspm.Value < this.currentRspmLimit)
                {
                    ConsoleWrite(">>> ADD KickTargetPlayer [" + soldierName + "] RSPM : " + playerRspm.Value);

                    // KICK対象に追加
                    this.kickTargetPlayer.Add(soldierName);
                }
                else
                {
                    // KICK対象から削除
                    this.kickTargetPlayer.Remove(soldierName);
                }
            }
            else
            {
                ConsoleWrite("[" + soldierName + "] RSPM Unknown !!");

                // プレイヤースタッツ取得
                PlayerStats playerStats = getPlayerStats(soldierName);
                if (playerStats == null)
                {
                    ConsoleWrite("[" + soldierName + "] SKILL Unknown !!!");
                }
                else
                {
                    ConsoleWrite("[" + soldierName + "] SKILL : " + playerStats.Skill.ToString());

                    if (playerStats.Skill < this.currentSkillLimit)
                    {
                        ConsoleWrite(">>> ADD KickTargetPlayer [" + soldierName + "] SKILL : " + playerStats.Skill.ToString());

                        // KICK対象に追加
                        this.kickTargetPlayer.Add(soldierName);
                    }
                    else
                    {
                        // KICK対象から削除
                        this.kickTargetPlayer.Remove(soldierName);
                    }
                }
            }
        }

        Thread.Sleep(GETSTATS_RETRY_DELAY);
    }
}

public override void OnPlayerLeft(CPlayerInfo playerInfo) {
}

public override void OnPlayerKilled(Kill kKillerVictimDetails) { }

public override void OnPlayerSpawned(string soldierName, Inventory spawnedInventory) { }

public override void OnPlayerTeamChange(string soldierName, int teamId, int squadId)
{
    if (this.kickTargetPlayer.Contains(soldierName))
    {
        // KICK対象から削除
        this.kickTargetPlayer.Remove(soldierName);

        new Thread(new ThreadStart(() =>
        {
            ConsoleWrite("<<<<< KICK PLAYER [" + soldierName + "]");

            Thread.Sleep(4000);

            // キック通知yellメッセージ
            yellPlayer(this.kickMessageSetting, "10", soldierName);

            Thread.Sleep(6000);

            // KICK !!!
            kickPlayer(soldierName, this.kickMessageSetting);

        })).Start();
    }
}

public override void OnGlobalChat(string speaker, string message) { }

public override void OnTeamChat(string speaker, string message, int teamId) { }

public override void OnSquadChat(string speaker, string message, int teamId, int squadId) { }

public override void OnRoundOverPlayers(List<CPlayerInfo> players) { }

public override void OnRoundOverTeamScores(List<TeamScore> teamScores) { }

public override void OnRoundOver(int winningTeamId)
{
    if (!String.IsNullOrEmpty(this.roundAverageMessage))
    {
        // ADMINメッセージ
        SendGlobalMessage(this.roundAverageMessage);
    }
}

public override void OnLoadingLevel(string mapFileName, int roundsPlayed, int roundsTotal) { }

public override void OnLevelStarted() { }

public override void OnLevelLoaded(string mapFileName, string Gamemode, int roundsPlayed, int roundsTotal)
{
    if (kickTargetPlayer != null)
    {
        ConsoleWrite("KickTargetPlayer Count = " + kickTargetPlayer.Count);
    }

    // 前のラウンドで平均スキル値を取れなかった場合
    if (!this.isShowedAverageSkill)
    {
        // デフォルトのRSPMリミットに初期化
        this.currentRspmLimit = this.rspmLimitSetting;
        // デフォルトのスキルリミットに初期化
        this.currentSkillLimit = this.skillLimitSetting;
    }

    // ラウンド開始時にフラグをクリアし、ラウンド中に1回だけ平均スキル値を表示する
    this.isShowedAverageSkill = false;
    this.roundAverageMessage = null;

    // 現在のゲームモードを保持(StatsNow API呼び出し用)
    this.currentGamemode = Gamemode;
    
}

/// <summary>
/// SET SERVER NAME
/// </summary>
/// <param name="newServerName"></param>
private void setServerName(string newServerName)
{
    this.ExecuteCommand("procon.protected.send", "vars.serverName", newServerName);
    ConsoleWrite("=== Server Name set to: '" + newServerName + "'");
}

/// <summary>
/// ADMIN KICKPLAYER
/// </summary>
/// <param name="soldierName"></param>
/// <param name="reason"></param>
private void kickPlayer(string soldierName, string reason)
{
    ExecuteCommand("procon.protected.send", "admin.kickPlayer", soldierName, reason);
}

/// <summary>
/// ADMIN YELL
/// </summary>
/// <param name="message"></param>
/// <param name="displayTime"></param>
/// <param name="soldierName"></param>
private void yellPlayer(string message, string displayTime, string soldierName)
{
    ExecuteCommand("procon.protected.send", "admin.yell", message, displayTime, "player", soldierName);
}

/// <summary>
/// ADMIN SAY
/// </summary>
/// <param name="message"></param>
private void SendGlobalMessage(String message)
{
    string pluginName = "(" + GetPluginName() + ") ";
    ServerCommand("admin.say", pluginName + message, "all");
}

/// <summary>
/// PROCON CHAT
/// </summary>
/// <param name="message"></param>
private void ProconChatMessage(string message)
{
    string pluginName = "(" + GetPluginName() + ") ";
    ExecuteCommand("procon.protected.chat.write", pluginName + message);
}

/// <summary>
/// プレイヤーのRSPMを取得
/// </summary>
/// <param name="soldierName"></param>
/// <returns></returns>
private int? getPlayerRspm(string soldierName)
{
    int? rspm = null;

    for (int i = 0; i < GETSTATS_RETRY_COUNT; i++)
    {
        try
        {
            // StatsNowのプレイヤーステータスを取得
            string statsnowUrl = "http://www.goodgames.jp/statsnow/bf4/main/" + soldierName;
            string statsnowPage = fetchWebPage(statsnowUrl);

            if (!String.IsNullOrEmpty(statsnowPage))
            {
                // プレイヤーのRSPM値を探す
                Match match = Regex.Match(statsnowPage, @"varChartPlayerRspm\s*=\s*\d+;", RegexOptions.IgnoreCase | RegexOptions.Singleline);
                if (match.Success)
                {
                    string statsnowRspmValue = match.Value;

                    string[] splitValues = statsnowRspmValue.Split('=');
                    if (splitValues.Length == 2)
                    {
                        string trimValue = splitValues[1].Replace(';', ' ').Trim();
                        rspm = int.Parse(trimValue);

                        break; // RSPM値取得成功
                    }
                }

                match = Regex.Match(statsnowPage, @"There isn't enough battle record.", RegexOptions.IgnoreCase | RegexOptions.Singleline);
                if (match.Success)
                {
                    //ConsoleWrite("There isn't enough battle record. (RSPM NODATA)");
                    break; // RSPMデータなし
                }
            }
        }
        catch (Exception e)
        {
            ConsoleWrite("FAILED getPlayerRspm [" + soldierName + "]. RETRY:" + (i + 1).ToString());
            ConsoleWrite(e.Message);
            ConsoleWrite(e.StackTrace);
        }

        Thread.Sleep(GETSTATS_RETRY_DELAY); // リトライ
    }

    return rspm;
}

/// <summary>
/// プレイヤーのRSPMを取得(StatsNow API)
/// </summary>
/// <param name="soldierName"></param>
/// <returns></returns>
private int? getPlayerRspmApi(string soldierName, string gameMode, int numRounds)
{
    int? rspm = null;

    for (int i = 0; i < GETSTATS_RETRY_COUNT; i++)
    {
        try
        {
            // StatsNowのプレイヤーステータスを取得
            string statsnowApiUrl = "http://www.goodgames.jp/statsnow/bf4/api/rspm?soldierName=" + soldierName + "&gameMode=" + gameMode + "&numRounds=" + numRounds;
            string statsnowJsonResp = fetchJsonApi(statsnowApiUrl);

            if (!String.IsNullOrEmpty(statsnowJsonResp))
            {
                var json = (Hashtable)JSON.JsonDecode(statsnowJsonResp);
                string rspmString = (string)json["rspm"];

                // プレイヤーのRSPM値を探す
                double rspmValue = Double.Parse(rspmString);
                if (rspmValue > 0)
                {
                    rspm = (int)rspmValue;
                    break; // RSPM値取得成功

                }
                else if (rspmValue == 0)
                {
                    //ConsoleWrite("DEBUGGG There isn't enough battle record. (RSPM NODATA)");
                    break; // RSPMデータなし
                }
            }
        }
        catch (Exception e)
        {
            ConsoleWrite("FAILED getPlayerRspmApi [" + soldierName + "]. RETRY:" + (i + 1).ToString());
            ConsoleWrite(e.Message);
            ConsoleWrite(e.StackTrace);
        }

        Thread.Sleep(GETSTATS_RETRY_DELAY); // リトライ
    }

    return rspm;
}
    
// <summary>
/// プレイヤーのスタッツ情報取得
/// </summary>
/// <param name="soldierName"></param>
/// <returns></returns>
private PlayerStats getPlayerStats(string soldierName)
{
    /* BF4Stats版はうまく動かなかった
    // BF4Stats APIのURL
    string url = String.Format(BF4STATS_PLAYERINFO_URL_BASE_JSON, soldierName);

    // APIコール
    string response = fetchWebPage(url);

    ConsoleWrite("Response Length:" + response.Length);

    // レスポンスのJSONをパース
    Hashtable json = (Hashtable)JSON.JsonDecode(response);
    ConsoleWrite("Success parse JSON Count:" + json.Count.ToString());

    Hashtable playerInfo = (Hashtable)json["player"];
    Hashtable statsInfo = (Hashtable)json["stats"];
    Hashtable extraInfo = (Hashtable)statsInfo["extra"];
    
        //playerStats.Id = (double)playerInfo["id"];
        //playerStats.Name = (string)playerInfo["name"];
        //playerStats.Rank = (double)statsInfo["rank"];
        //playerStats.Skill = (double)statsInfo["skill"];
        //playerStats.Spm = (double)extraInfo["spm"];
        //playerStats.Kdr = (double)extraInfo["kdr"];
        //playerStats.Wlr = (double)extraInfo["wlr"];
        //playerStats.Kpm = (double)extraInfo["kpm"];
    */

    PlayerStats playerStats = null;

    for (int i = 0; i < GETSTATS_RETRY_COUNT; i++)
    {
        try
        {
            // バトルログのユーザページを取得
            string bf4userUrl = "http://battlelog.battlefield.com/bf4/user/" + soldierName;
            string bf4UserPage = fetchWebPage(bf4userUrl);

            // プレイヤーのPersonaIdを探す
            MatchCollection pid = Regex.Matches(bf4UserPage, @"bf4/soldier/" + soldierName + @"/stats/(\d+)(['""]|/\s*['""]|/[^/'""]+)", RegexOptions.IgnoreCase | RegexOptions.Singleline);

            // PC用のPersonaIdを探す
            String personaId = null;
            foreach (Match match in pid)
            {
                if (match.Success && !Regex.Match(match.Groups[2].Value.Trim(), @"(ps3|xbox)", RegexOptions.IgnoreCase).Success)
                {
                    personaId = match.Groups[1].Value.Trim();
                    break;
                }
            }

            //ConsoleWrite("[" + soldierName + "] PersonaId : " + personaId);

            if (personaId != null)
            {
                // バトルログのスタッツ詳細を取得
                string bf4StatsDetailUrl = "http://battlelog.battlefield.com/bf4/warsawdetailedstatspopulate/" + personaId + "/1/";
                string battlelogResp = fetchWebPage(bf4StatsDetailUrl);

                // JSONから必要な情報を取得
                Hashtable battlelogJson = (Hashtable)JSON.JsonDecode(battlelogResp);
                //ConsoleWrite("Success parse Battlelog JSON Count:" + battlelogJson.Count.ToString());

                Hashtable dataInfo = (Hashtable)battlelogJson["data"];
                Hashtable generalStatsInfo = (Hashtable)dataInfo["generalStats"];

                int rank = int.Parse((string)generalStatsInfo["rank"]);
                int skill = int.Parse((string)generalStatsInfo["skill"]);

                // プレイヤーのスタッツ取得
                playerStats = new PlayerStats();
                playerStats.PersonaId = personaId;
                playerStats.Name = soldierName;
                playerStats.Rank = rank;
                playerStats.Skill = skill;

                break; // スタッツ取得成功
            }
        }
        catch (Exception e)
        {
            ConsoleWrite("FAILED getPlayerStats [" + soldierName + "]. RETRY:" + (i + 1).ToString());
            ConsoleWrite(e.Message);
            ConsoleWrite(e.StackTrace);
        }

        Thread.Sleep(GETSTATS_RETRY_DELAY); // リトライ
    }

    return playerStats;
}

/// <summary>
/// サーバから文字列をダウンロード
/// </summary>
/// <param name="url"></param>
/// <returns></returns>
private string fetchWebPage(string url)
{
    return fetchWebPageCore(url, null);
}

/// <summary>
/// JSON文字列をダウンロード
/// </summary>
/// <param name="url"></param>
/// <returns></returns>
private string fetchJsonApi(string url)
{
    return fetchWebPageCore(url, JSON_REQUEST_HEADERS);
}

/// <summary>
/// サーバページ取得
/// </summary>
/// <param name="url"></param>
/// <returns></returns>
private string fetchWebPageCore(string url, Dictionary<string, string> headers)
{
    try
    {
        WebClient client = new WebClient();

        if (headers != null)
        {
            foreach (var header in headers)
            {
                // HttpHeaderをセットする
                client.Headers.Add(header.Key, header.Value);
            }
        }

        for (int i = 0; i < WEB_RETRY_COUNT; i++)
        {
            DateTime since = DateTime.Now;

            try
            {
                string response = client.DownloadString(url);

                return response;
            }
            catch (WebException we)
            {
                if (we.Status.Equals(WebExceptionStatus.Timeout))
                {
                    ConsoleWrite("HTTP request timed-out:" + i);
                    Thread.Sleep(WEB_RETRY_DELAY);
                }
                else
                {
                    throw we;
                }
            }
            finally
            {
                ConsoleWrite("^2^bTIME^n took " + DateTime.Now.Subtract(since).TotalSeconds.ToString("F2") + " secs, fetchWebPage: " + url);
            }
        }

        return String.Empty;
    }
    catch (Exception e)
    {
        throw e;
    }
}

} // end BFJC_SkillLimiter

/// <summary>
/// プレイヤースタッツ
/// </summary>
public class PlayerStats
{
    public string PersonaId { get; set; }
    public string Name { get; set; }
    public int Rank { get; set; }
    public int Skill { get; set; }
    public double Spm { get; set; }
    public double Kdr { get; set; }
    public double Wlr { get; set; }
    public double Kpm { get; set; }
}

} // end namespace PRoConEvents



