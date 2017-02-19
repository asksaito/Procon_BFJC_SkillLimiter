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
/// �X�^�b�c�t�F�b�`�X���b�h
/// </summary>
private Thread statsFetchThread;

/// <summary>
/// �X�^�b�c�t�F�b�`�X���b�h�L�����Z���t���O
/// </summary>
private bool isCancelStatsFetchThread = false;

/// <summary>
/// �X�^�b�c�擾�L���[
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

    // JSON���N�G�X�g�w�b�_(�g���܂킷�̂ł����ŏ�����)
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
        // �X���b�h�L�����Z�����N�G�X�g
        this.isCancelStatsFetchThread = true;
        // �X���b�h���I������܂ő҂�
        this.statsFetchThread.Join(5000);
    }
    playerJoinQueue = null;
	ConsoleWrite("Disabled!");
}


public override void OnVersion(string serverType, string version) { }

public override void OnServerInfo(CServerInfo serverInfo) {
    // �T�[�o��̃v���C���[����ێ�
    this.serverPlayerCount = serverInfo.PlayerCount;
    // �T�[�o�̍ő�l����ێ��i�R�}���_�[�����������j
    this.serverMaxPlayerCount = serverInfo.MaxPlayerCount;

    /*
     * �T�[�o���̕ύX 
     */
    if (this.serverPlayerCount < this.limitEnabledPlayerNumSetting)
    {
        if (serverInfo.ServerName != this.limiterOffServerNameSetting)
        {
            // ���~�b�^�[OFF���̃T�[�o���ɕύX
            setServerName(this.limiterOffServerNameSetting);
        }

        // �S�~���c���Ă���\��������̂ŃN���A����
        if (kickTargetPlayer.Count != 0) { kickTargetPlayer.Clear(); }
    }
    else
    {
        // ���~�b�^�[ON���̃T�[�o��
        //string newServerName = String.Format(this.limiterOnServerNameSetting, this.currentSkillLimit);
        string newServerName = String.Format(this.limiterOnServerNameSetting, this.currentRspmLimit);

        if (serverInfo.ServerName != newServerName)
        {
            // ���~�b�^�[ON���̃T�[�o���ɕύX
            setServerName(newServerName);
        }
    }
}

public override void OnResponseError(List<string> requestWords, string error) { }

public override void OnListPlayers(List<CPlayerInfo> players, CPlayerSubset subset)
{
    if (!this.isShowedAverageSkill && players.Count >= this.serverMaxPlayerCount)
    {
        // ���σX�L���\���ς݃t���O
        this.isShowedAverageSkill = true;

        // ���σX�L���v�Z�X���b�h�N��
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
                    // RSPM�l�̍��v
                    sumRspm += playerRspm.Value;

                    // RSPM�l���擾�o�����l��
                    availableRspmCnt++;

                    ConsoleWrite("(CalcThread) [" + player.SoldierName + "] RSPM : " + playerRspm.Value + " Sum: " + sumRspm + " Cnt: " + availableRspmCnt);
                }

                Thread.Sleep(1500);

                PlayerStats playerStats = getPlayerStats(player.SoldierName);
                if (playerStats != null)
                {
                    // �X�L���l�̍��v
                    sumSkill += playerStats.Skill;

                    // �X�L���l���擾�o�����l��
                    availableStatsCnt++;

                    ConsoleWrite("(CalcThread) [" + player.SoldierName + "] Skill: " + playerStats.Skill + " Sum: " + sumSkill + " Cnt: " + availableStatsCnt);
                }

                Thread.Sleep(1500); // 3�b�Ɉ�l����
            }

            if (availableRspmCnt > 0)
            {
                // RSPM���ϒl
                int average = sumRspm / availableRspmCnt;

                // ���݂�RSPM���~�b�g�l���X�V
                this.currentRspmLimit = average * this.dynamicRspmLimitSetting / 100; // ����RSPM��N%
                if (this.currentRspmLimit > this.maxRspmLimitSetting)
                {
                    // RSPM�̍ő�l
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

                //// ADMIN���b�Z�[�W
                //SendGlobalMessage(message.ToString());
                // PROCON�`���b�g���b�Z�[�W
                ProconChatMessage(message.ToString());

                // ���E���h�̕��ϒl���b�Z�[�W�ێ�
                this.roundAverageMessage = message.ToString();
            }

            if (availableStatsCnt > 0)
            {
                // �X�L�����ϒl
                int average = sumSkill / availableStatsCnt;

                // ���݂̃X�L�����~�b�g�l���X�V
                this.currentSkillLimit = average * this.dynamicSkillLimitSetting / 100; // ���σX�L����N%
                if (this.currentSkillLimit > this.maxSkillLimitSetting)
                {
                    // �X�L���̍ő�l
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

                //// ADMIN���b�Z�[�W
                //SendGlobalMessage(message.ToString());
                // PROCON�`���b�g���b�Z�[�W
                ProconChatMessage(message.ToString());
            }
        })).Start();
    }
}

public override void OnPlayerJoin(string soldierName)
{
    if (this.serverPlayerCount >= this.limitEnabledPlayerNumSetting)
    {
        // �v���C���[�X�^�b�c�擾�L���[�ɒǉ�
        this.playerJoinQueue.Enqueue(soldierName);
    }
}

/// <summary>
/// �v���C���[�̃X�^�b�c���擾����X���b�h
/// </summary>
private void fetchPlayerStats()
{
    while (!this.isCancelStatsFetchThread)
    {
        if (this.playerJoinQueue.Count > 0)
        {
            ConsoleWrite("^2^bQUEUE^n " + this.playerJoinQueue.Count + " still in queue. NowRspmLimit: " + this.currentRspmLimit + " / NowSkillLimit: " + this.currentSkillLimit);

            // �L���[����v���C���[���擾
            string soldierName = this.playerJoinQueue.Dequeue();

            ConsoleWrite("Getting player STATS for [" + soldierName + "]");

            int? playerRspm = getPlayerRspmApi(soldierName, this.currentGamemode, STATSNOW_RSPM_API_GET_NUMROUNDS);
            if (playerRspm != null)
            {
                // RSPM�擾����
                ConsoleWrite("[" + soldierName + "] RSPM : " + playerRspm.Value);

                if (playerRspm.Value < this.currentRspmLimit)
                {
                    ConsoleWrite(">>> ADD KickTargetPlayer [" + soldierName + "] RSPM : " + playerRspm.Value);

                    // KICK�Ώۂɒǉ�
                    this.kickTargetPlayer.Add(soldierName);
                }
                else
                {
                    // KICK�Ώۂ���폜
                    this.kickTargetPlayer.Remove(soldierName);
                }
            }
            else
            {
                ConsoleWrite("[" + soldierName + "] RSPM Unknown !!");

                // �v���C���[�X�^�b�c�擾
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

                        // KICK�Ώۂɒǉ�
                        this.kickTargetPlayer.Add(soldierName);
                    }
                    else
                    {
                        // KICK�Ώۂ���폜
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
        // KICK�Ώۂ���폜
        this.kickTargetPlayer.Remove(soldierName);

        new Thread(new ThreadStart(() =>
        {
            ConsoleWrite("<<<<< KICK PLAYER [" + soldierName + "]");

            Thread.Sleep(4000);

            // �L�b�N�ʒmyell���b�Z�[�W
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
        // ADMIN���b�Z�[�W
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

    // �O�̃��E���h�ŕ��σX�L���l�����Ȃ������ꍇ
    if (!this.isShowedAverageSkill)
    {
        // �f�t�H���g��RSPM���~�b�g�ɏ�����
        this.currentRspmLimit = this.rspmLimitSetting;
        // �f�t�H���g�̃X�L�����~�b�g�ɏ�����
        this.currentSkillLimit = this.skillLimitSetting;
    }

    // ���E���h�J�n���Ƀt���O���N���A���A���E���h����1�񂾂����σX�L���l��\������
    this.isShowedAverageSkill = false;
    this.roundAverageMessage = null;

    // ���݂̃Q�[�����[�h��ێ�(StatsNow API�Ăяo���p)
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
/// �v���C���[��RSPM���擾
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
            // StatsNow�̃v���C���[�X�e�[�^�X���擾
            string statsnowUrl = "http://www.goodgames.jp/statsnow/bf4/main/" + soldierName;
            string statsnowPage = fetchWebPage(statsnowUrl);

            if (!String.IsNullOrEmpty(statsnowPage))
            {
                // �v���C���[��RSPM�l��T��
                Match match = Regex.Match(statsnowPage, @"varChartPlayerRspm\s*=\s*\d+;", RegexOptions.IgnoreCase | RegexOptions.Singleline);
                if (match.Success)
                {
                    string statsnowRspmValue = match.Value;

                    string[] splitValues = statsnowRspmValue.Split('=');
                    if (splitValues.Length == 2)
                    {
                        string trimValue = splitValues[1].Replace(';', ' ').Trim();
                        rspm = int.Parse(trimValue);

                        break; // RSPM�l�擾����
                    }
                }

                match = Regex.Match(statsnowPage, @"There isn't enough battle record.", RegexOptions.IgnoreCase | RegexOptions.Singleline);
                if (match.Success)
                {
                    //ConsoleWrite("There isn't enough battle record. (RSPM NODATA)");
                    break; // RSPM�f�[�^�Ȃ�
                }
            }
        }
        catch (Exception e)
        {
            ConsoleWrite("FAILED getPlayerRspm [" + soldierName + "]. RETRY:" + (i + 1).ToString());
            ConsoleWrite(e.Message);
            ConsoleWrite(e.StackTrace);
        }

        Thread.Sleep(GETSTATS_RETRY_DELAY); // ���g���C
    }

    return rspm;
}

/// <summary>
/// �v���C���[��RSPM���擾(StatsNow API)
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
            // StatsNow�̃v���C���[�X�e�[�^�X���擾
            string statsnowApiUrl = "http://www.goodgames.jp/statsnow/bf4/api/rspm?soldierName=" + soldierName + "&gameMode=" + gameMode + "&numRounds=" + numRounds;
            string statsnowJsonResp = fetchJsonApi(statsnowApiUrl);

            if (!String.IsNullOrEmpty(statsnowJsonResp))
            {
                var json = (Hashtable)JSON.JsonDecode(statsnowJsonResp);
                string rspmString = (string)json["rspm"];

                // �v���C���[��RSPM�l��T��
                double rspmValue = Double.Parse(rspmString);
                if (rspmValue > 0)
                {
                    rspm = (int)rspmValue;
                    break; // RSPM�l�擾����

                }
                else if (rspmValue == 0)
                {
                    //ConsoleWrite("DEBUGGG There isn't enough battle record. (RSPM NODATA)");
                    break; // RSPM�f�[�^�Ȃ�
                }
            }
        }
        catch (Exception e)
        {
            ConsoleWrite("FAILED getPlayerRspmApi [" + soldierName + "]. RETRY:" + (i + 1).ToString());
            ConsoleWrite(e.Message);
            ConsoleWrite(e.StackTrace);
        }

        Thread.Sleep(GETSTATS_RETRY_DELAY); // ���g���C
    }

    return rspm;
}
    
// <summary>
/// �v���C���[�̃X�^�b�c���擾
/// </summary>
/// <param name="soldierName"></param>
/// <returns></returns>
private PlayerStats getPlayerStats(string soldierName)
{
    /* BF4Stats�ł͂��܂������Ȃ�����
    // BF4Stats API��URL
    string url = String.Format(BF4STATS_PLAYERINFO_URL_BASE_JSON, soldierName);

    // API�R�[��
    string response = fetchWebPage(url);

    ConsoleWrite("Response Length:" + response.Length);

    // ���X�|���X��JSON���p�[�X
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
            // �o�g�����O�̃��[�U�y�[�W���擾
            string bf4userUrl = "http://battlelog.battlefield.com/bf4/user/" + soldierName;
            string bf4UserPage = fetchWebPage(bf4userUrl);

            // �v���C���[��PersonaId��T��
            MatchCollection pid = Regex.Matches(bf4UserPage, @"bf4/soldier/" + soldierName + @"/stats/(\d+)(['""]|/\s*['""]|/[^/'""]+)", RegexOptions.IgnoreCase | RegexOptions.Singleline);

            // PC�p��PersonaId��T��
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
                // �o�g�����O�̃X�^�b�c�ڍׂ��擾
                string bf4StatsDetailUrl = "http://battlelog.battlefield.com/bf4/warsawdetailedstatspopulate/" + personaId + "/1/";
                string battlelogResp = fetchWebPage(bf4StatsDetailUrl);

                // JSON����K�v�ȏ����擾
                Hashtable battlelogJson = (Hashtable)JSON.JsonDecode(battlelogResp);
                //ConsoleWrite("Success parse Battlelog JSON Count:" + battlelogJson.Count.ToString());

                Hashtable dataInfo = (Hashtable)battlelogJson["data"];
                Hashtable generalStatsInfo = (Hashtable)dataInfo["generalStats"];

                int rank = int.Parse((string)generalStatsInfo["rank"]);
                int skill = int.Parse((string)generalStatsInfo["skill"]);

                // �v���C���[�̃X�^�b�c�擾
                playerStats = new PlayerStats();
                playerStats.PersonaId = personaId;
                playerStats.Name = soldierName;
                playerStats.Rank = rank;
                playerStats.Skill = skill;

                break; // �X�^�b�c�擾����
            }
        }
        catch (Exception e)
        {
            ConsoleWrite("FAILED getPlayerStats [" + soldierName + "]. RETRY:" + (i + 1).ToString());
            ConsoleWrite(e.Message);
            ConsoleWrite(e.StackTrace);
        }

        Thread.Sleep(GETSTATS_RETRY_DELAY); // ���g���C
    }

    return playerStats;
}

/// <summary>
/// �T�[�o���當������_�E�����[�h
/// </summary>
/// <param name="url"></param>
/// <returns></returns>
private string fetchWebPage(string url)
{
    return fetchWebPageCore(url, null);
}

/// <summary>
/// JSON��������_�E�����[�h
/// </summary>
/// <param name="url"></param>
/// <returns></returns>
private string fetchJsonApi(string url)
{
    return fetchWebPageCore(url, JSON_REQUEST_HEADERS);
}

/// <summary>
/// �T�[�o�y�[�W�擾
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
                // HttpHeader���Z�b�g����
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
/// �v���C���[�X�^�b�c
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



