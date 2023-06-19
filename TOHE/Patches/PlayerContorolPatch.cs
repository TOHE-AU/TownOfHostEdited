using AmongUs.GameOptions;
using HarmonyLib;
using Hazel;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TOHE.Modules;
using TOHE.Roles.AddOns.Crewmate;
using TOHE.Roles.Core;
using TOHE.Roles.Impostor;
using TOHE.Roles.Neutral;
using UnityEngine;
using static TOHE.Translator;

namespace TOHE;

[HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.CheckProtect))]
class CheckProtectPatch
{
    public static bool Prefix(PlayerControl __instance, [HarmonyArgument(0)] PlayerControl target)
    {
        if (!AmongUsClient.Instance.AmHost) return false;
        Logger.Info("CheckProtect発生: " + __instance.GetNameWithRole() + "=>" + target.GetNameWithRole(), "CheckProtect");
        if (__instance.Is(CustomRoles.Sheriff))
        {
            if (__instance.Data.IsDead)
            {
                Logger.Info("守護をブロックしました。", "CheckProtect");
                return false;
            }
        }
        return true;
    }
}
[HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.CheckMurder))]
class CheckMurderPatch
{
    public static Dictionary<byte, float> TimeSinceLastKill = new();
    public static void Update()
    {
        for (byte i = 0; i < 15; i++)
        {
            if (TimeSinceLastKill.ContainsKey(i))
            {
                TimeSinceLastKill[i] += Time.deltaTime;
                if (15f < TimeSinceLastKill[i]) TimeSinceLastKill.Remove(i);
            }
        }
    }
    public static bool Prefix(PlayerControl __instance, [HarmonyArgument(0)] PlayerControl target)
    {
        if (!AmongUsClient.Instance.AmHost) return false;

        // 処理は全てCustomRoleManager側で行う
        CustomRoleManager.OnCheckMurder(__instance, target);

        return false;
    }

    // 不正キル防止チェック
    public static bool CheckForInvalidMurdering(MurderInfo info)
    {
        (var killer, var target) = info.AttemptTuple;

        // Killerが既に死んでいないかどうか
        if (!killer.IsAlive())
        {
            Logger.Info($"{killer.GetNameWithRole()}は死亡しているためキャンセルされました。", "CheckMurder");
            return false;
        }
        // targetがキル可能な状態か
        if (
            // PlayerDataがnullじゃないか確認
            target.Data == null ||
            // targetの状態をチェック
            target.inVent ||
            target.MyPhysics.Animations.IsPlayingEnterVentAnimation() ||
            target.MyPhysics.Animations.IsPlayingAnyLadderAnimation() ||
            target.inMovingPlat)
        {
            Logger.Info("targetは現在キルできない状態です。", "CheckMurder");
            return false;
        }
        // targetが既に死んでいないか
        if (!target.IsAlive())
        {
            Logger.Info("targetは既に死んでいたため、キルをキャンセルしました。", "CheckMurder");
            return false;
        }
        // 会議中のキルでないか
        if (MeetingHud.Instance != null)
        {
            Logger.Info("会議が始まっていたため、キルをキャンセルしました。", "CheckMurder");
            return false;
        }

        // 連打キルでないか
        float minTime = Mathf.Max(0.02f, AmongUsClient.Instance.Ping / 1000f * 6f); //※AmongUsClient.Instance.Pingの値はミリ秒(ms)なので÷1000
                                                                                    //TimeSinceLastKillに値が保存されていない || 保存されている時間がminTime以上 => キルを許可
                                                                                    //↓許可されない場合
        if (TimeSinceLastKill.TryGetValue(killer.PlayerId, out var time) && time < minTime)
        {
            Logger.Info("前回のキルからの時間が早すぎるため、キルをブロックしました。", "CheckMurder");
            return false;
        }
        TimeSinceLastKill[killer.PlayerId] = 0f;

        // キルが可能なプレイヤーか(遠隔は除く)
        if (!info.IsFakeSuicide && !killer.CanUseKillButton())
        {
            Logger.Info(killer.GetNameWithRole() + "はKillできないので、キルはキャンセルされました。", "CheckMurder");
            return false;
        }

        // SoloKombat 的击杀处理
        if (Options.CurrentGameMode == CustomGameMode.SoloKombat)
        {
            SoloKombatManager.OnPlayerAttack(killer, target);
            return false;
        }

        return true;
    }
}
[HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.MurderPlayer))]
class MurderPlayerPatch
{
    public static void Prefix(PlayerControl __instance, [HarmonyArgument(0)] PlayerControl target)
    {
        Logger.Info($"{__instance.GetNameWithRole()} => {target.GetNameWithRole()}{(target.protectedByGuardian ? "(Protected)" : "")}", "MurderPlayer");

        if (RandomSpawn.CustomNetworkTransformPatch.NumOfTP.TryGetValue(__instance.PlayerId, out var num) && num > 2) RandomSpawn.CustomNetworkTransformPatch.NumOfTP[__instance.PlayerId] = 3;
        if (!target.protectedByGuardian)
            Camouflage.RpcSetSkin(target, ForceRevert: true);
    }
    public static void Postfix(PlayerControl __instance, [HarmonyArgument(0)] PlayerControl target)
    {
        if (target.AmOwner) RemoveDisableDevicesPatch.UpdateDisableDevices();
        if (!target.Data.IsDead || !AmongUsClient.Instance.AmHost) return;
        //以降ホストしか処理しない
        // 処理は全てCustomRoleManager側で行う
        CustomRoleManager.OnMurderPlayer(__instance, target);

        //看看UP是不是被首刀了
        if (Main.FirstDied == byte.MaxValue && target.Is(CustomRoles.Youtuber))
        {
            CustomSoundsManager.RPCPlayCustomSoundAll("Congrats");
            CustomWinnerHolder.ResetAndSetWinner(CustomWinner.Youtuber); //UP主被首刀了，哈哈哈哈哈
            CustomWinnerHolder.WinnerIds.Add(target.PlayerId);
        }

        //记录首刀
        if (Main.FirstDied == byte.MaxValue)
            Main.FirstDied = target.PlayerId;
    }
}
[HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.Shapeshift))]
class ShapeshiftPatch
{
    public static void Prefix(PlayerControl __instance, [HarmonyArgument(0)] PlayerControl target)
    {
        Logger.Info($"{__instance?.GetNameWithRole()} => {target?.GetNameWithRole()}", "Shapeshift");

        var shapeshifter = __instance;
        var shapeshifting = shapeshifter.PlayerId != target.PlayerId;

        if (Main.CheckShapeshift.TryGetValue(shapeshifter.PlayerId, out var last) && last == shapeshifting)
        {
            Logger.Info($"{__instance?.GetNameWithRole()}:Cancel Shapeshift.Prefix", "Shapeshift");
            return;
        }

        Main.CheckShapeshift[shapeshifter.PlayerId] = shapeshifting;
        Main.ShapeshiftTarget[shapeshifter.PlayerId] = target.PlayerId;

        if (shapeshifter.IsEaten() || GameStates.IsVoting || !target.IsAlive())
            goto End;

        shapeshifter.GetRoleClass()?.OnShapeshift(target);

    End:

        if (!AmongUsClient.Instance.AmHost) return;

        if (!shapeshifting) Camouflage.RpcSetSkin(__instance);

        //変身解除のタイミングがずれて名前が直せなかった時のために強制書き換え
        if (!shapeshifting)
        {
            new LateTask(() =>
            {
                Utils.NotifyRoles(NoCache: true);
            },
            1.2f, "ShapeShiftNotify");
        }
    }
}
[HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.ReportDeadBody))]
class ReportDeadBodyPatch
{
    public static Dictionary<byte, bool> CanReport;
    public static Dictionary<byte, List<GameData.PlayerInfo>> WaitReport = new();
    public static bool Prefix(PlayerControl __instance, [HarmonyArgument(0)] GameData.PlayerInfo target)
    {
        if (GameStates.IsMeeting) return false;
        if (Options.DisableMeeting.GetBool()) return false;
        if (Options.CurrentGameMode == CustomGameMode.SoloKombat) return false;
        if (!CanReport[__instance.PlayerId])
        {
            WaitReport[__instance.PlayerId].Add(target);
            Logger.Warn($"{__instance.GetNameWithRole()}:通報禁止中のため可能になるまで待機します", "ReportDeadBody");
            return false;
        }
        Logger.Info($"{__instance.GetNameWithRole()} => {target?.Object?.GetNameWithRole() ?? "null"}", "ReportDeadBody");
        if (!AmongUsClient.Instance.AmHost) return true;

        //通報者が死んでいる場合、本処理で会議がキャンセルされるのでここで止める
        if (__instance.Data.IsDead) return false;

        if (Options.SyncButtonMode.GetBool() && target == null)
        {
            Logger.Info("最大:" + Options.SyncedButtonCount.GetInt() + ", 現在:" + Options.UsedButtonCount, "ReportDeadBody");
            if (Options.SyncedButtonCount.GetFloat() <= Options.UsedButtonCount)
            {
                Logger.Info("使用可能ボタン回数が最大数を超えているため、ボタンはキャンセルされました。", "ReportDeadBody");
                return false;
            }
            else Options.UsedButtonCount++;
            if (Options.SyncedButtonCount.GetFloat() == Options.UsedButtonCount)
            {
                Logger.Info("使用可能ボタン回数が最大数に達しました。", "ReportDeadBody");
            }
        }

        foreach (var role in CustomRoleManager.AllActiveRoles.Values)
        {
            if (role.OnCheckReportDeadBody(__instance, target) == false)
            {
                Logger.Info($"会议被 {role.Player.GetNameWithRole()} 取消", "ReportDeadBody");
                return false;
            }
        }

        //=============================================
        //以下、ボタンが押されることが確定したものとする。
        //=============================================

        foreach (var role in CustomRoleManager.AllActiveRoles.Values)
        {
            role.OnReportDeadBody(__instance, target);
        }

        Main.AllPlayerControls
                    .Where(pc => Main.CheckShapeshift.ContainsKey(pc.PlayerId))
                    .Do(pc => Camouflage.RpcSetSkin(pc, RevertToDefault: true));
        MeetingTimeManager.OnReportDeadBody();

        Utils.NotifyRoles(isForMeeting: true, NoCache: true);

        Utils.SyncAllSettings();

        return true;
    }
    public static async void ChangeLocalNameAndRevert(string name, int time)
    {
        //async Taskじゃ警告出るから仕方ないよね。
        var revertName = PlayerControl.LocalPlayer.name;
        PlayerControl.LocalPlayer.RpcSetNameEx(name);
        await Task.Delay(time);
        PlayerControl.LocalPlayer.RpcSetNameEx(revertName);
    }
}
[HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.StartMeeting))]
public static class PlayerControlStartMeetingPatch
{
    public static void Prefix()
    {
        foreach (var kvp in PlayerState.AllPlayerStates)
        {
            var pc = Utils.GetPlayerById(kvp.Key);
            kvp.Value.LastRoom = pc.GetPlainShipRoom();
        }
    }
}
[HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.FixedUpdate))]
class FixedUpdatePatch
{
    private static StringBuilder Mark = new(20);
    private static StringBuilder Suffix = new(120);
    private static int LevelKickBufferTime = 10;
    public static void Postfix(PlayerControl __instance)
    {
        var player = __instance;

        if (!GameStates.IsModHost) return;

        Zoom.OnFixedUpdate();
        NameNotifyManager.OnFixedUpdate(player);
        TargetArrow.OnFixedUpdate(player);
        LocateArrow.OnFixedUpdate(player);

        CustomRoleManager.OnFixedUpdate(player);

        if (AmongUsClient.Instance.AmHost)
        {//実行クライアントがホストの場合のみ実行
            if (GameStates.IsLobby && ((ModUpdater.hasUpdate && ModUpdater.forceUpdate) || ModUpdater.isBroken || !Main.AllowPublicRoom || !VersionChecker.IsSupported) && AmongUsClient.Instance.IsGamePublic)
                AmongUsClient.Instance.ChangeGamePublic(false);

            if (GameStates.IsInTask && ReportDeadBodyPatch.CanReport[__instance.PlayerId] && ReportDeadBodyPatch.WaitReport[__instance.PlayerId].Count > 0)
            {
                var info = ReportDeadBodyPatch.WaitReport[__instance.PlayerId][0];
                ReportDeadBodyPatch.WaitReport[__instance.PlayerId].Clear();
                Logger.Info($"{__instance.GetNameWithRole()}:通報可能になったため通報処理を行います", "ReportDeadbody");
                __instance.ReportDeadBody(info);
            }

            //踢出低等级的人
            if (GameStates.IsLobby && !player.AmOwner && Options.KickLowLevelPlayer.GetInt() != 0 && (
                (player.Data.PlayerLevel != 0 && player.Data.PlayerLevel < Options.KickLowLevelPlayer.GetInt()) ||
                player.Data.FriendCode == ""
                ))
            {
                LevelKickBufferTime--;
                if (LevelKickBufferTime <= 0)
                {
                    LevelKickBufferTime = 100;
                    Utils.KickPlayer(player.GetClientId(), false, "LowLevel");
                    string msg = string.Format(GetString("KickBecauseLowLevel"), player.GetRealName().RemoveHtmlTags());
                    RPC.NotificationPop(msg);
                    Logger.Info(msg, "LowLevel Kick");
                }
            }

            DoubleTrigger.OnFixedUpdate(player);

            //ターゲットのリセット
            if (GameStates.IsInTask && player.IsAlive() && Options.LadderDeath.GetBool())
            {
                FallFromLadder.FixedUpdate(player);
            }

            if (GameStates.IsInGame) LoversSuicide();

            if (GameStates.IsInGame && player.AmOwner)
                DisableDevice.FixedUpdate();

            NameTagManager.ApplyFor(player);
        }
        //LocalPlayer専用
        if (__instance.AmOwner)
        {
            //キルターゲットの上書き処理
            if (GameStates.IsInTask && !__instance.Is(CustomRoleTypes.Impostor) && __instance.CanUseKillButton() && !__instance.Data.IsDead)
            {
                var players = __instance.GetPlayersInAbilityRangeSorted(false);
                PlayerControl closest = players.Count <= 0 ? null : players[0];
                HudManager.Instance.KillButton.SetTarget(closest);
            }
        }

        //役職テキストの表示
        var RoleTextTransform = __instance.cosmetics.nameText.transform.Find("RoleText");
        var RoleText = RoleTextTransform.GetComponent<TMPro.TextMeshPro>();
        if (RoleText != null && __instance != null)
        {
            if (GameStates.IsLobby)
            {
                if (Main.playerVersion.TryGetValue(__instance.PlayerId, out var ver))
                {
                    if (Main.ForkId != ver.forkId) // フォークIDが違う場合
                        __instance.cosmetics.nameText.text = $"<color=#ff0000><size=1.5>{ver.forkId}</size>\n{__instance?.name}</color>";
                    else if (Main.version.CompareTo(ver.version) == 0)
                        __instance.cosmetics.nameText.text = ver.tag == $"{ThisAssembly.Git.Commit}({ThisAssembly.Git.Branch})" ? $"<color=#fcc5f8>{__instance.name}</color>" : $"<color=#ffff00><size=1.5>{ver.tag}</size>\n{__instance?.name}</color>";
                    else __instance.cosmetics.nameText.text = $"<color=#ff0000><size=1.5>v{ver.version}</size>\n{__instance?.name}</color>";
                }
                else __instance.cosmetics.nameText.text = __instance?.Data?.PlayerName;
            }
            if (GameStates.IsInGame)
            {
                //if (Options.CurrentGameMode == CustomGameMode.HideAndSeek)
                //{
                //    var hasRole = main.AllPlayerCustomRoles.TryGetValue(__instance.PlayerId, out var role);
                //    if (hasRole) RoleTextData = Utils.GetRoleTextHideAndSeek(__instance.Data.Role.Role, role);
                //}
                (RoleText.enabled, RoleText.text) = Utils.GetRoleNameAndProgressTextData(PlayerControl.LocalPlayer, __instance);
                if (!AmongUsClient.Instance.IsGameStarted && AmongUsClient.Instance.NetworkMode != NetworkModes.FreePlay)
                {
                    RoleText.enabled = false; //ゲームが始まっておらずフリープレイでなければロールを非表示
                    if (!__instance.AmOwner) __instance.cosmetics.nameText.text = __instance?.Data?.PlayerName;
                }

                //変数定義
                var seer = PlayerControl.LocalPlayer;
                var seerRole = seer.GetRoleClass();
                var target = __instance;
                string RealName;
                Mark.Clear();
                Suffix.Clear();

                //名前変更
                RealName = target.GetRealName();

                //名前色変更処理
                //自分自身の名前の色を変更
                if (target.AmOwner && GameStates.IsInTask)
                { //targetが自分自身
                    if (target.Is(CustomRoles.Arsonist) && Arsonist.IsDouseDone(target))
                        RealName = Utils.ColorString(Utils.GetRoleColor(CustomRoles.Arsonist), GetString("EnterVentToWin"));
                    //TODO: FIXME
                    //if (target.Is(CustomRoles.Revolutionist) && target.IsDrawDone())
                    //    RealName = Utils.ColorString(Utils.GetRoleColor(CustomRoles.Revolutionist), string.Format(GetString("EnterVentWinCountDown"), Main.RevolutionistCountdown.TryGetValue(seer.PlayerId, out var x) ? x : 10));
                    if (seer.IsEaten())
                        RealName = Utils.ColorString(Utils.GetRoleColor(CustomRoles.Pelican), GetString("EatenByPelican"));
                    if (Options.CurrentGameMode == CustomGameMode.SoloKombat)
                        SoloKombatManager.GetNameNotify(target, ref RealName);
                    if (NameNotifyManager.GetNameNotify(target, out var name))
                        RealName = name;
                }

                //NameColorManager準拠の処理
                RealName = RealName.ApplyNameColorData(seer, target, false);

                //seer役職が対象のMark
                Mark.Append(seerRole?.GetMark(seer, target, false));
                //seerに関わらず発動するMark
                Mark.Append(CustomRoleManager.GetMarkOthers(seer, target, false));

                //ハートマークを付ける(会議中MOD視点)
                if (__instance.Is(CustomRoles.Lovers) && PlayerControl.LocalPlayer.Is(CustomRoles.Lovers))
                {
                    Mark.Append($"<color={Utils.GetRoleColorCode(CustomRoles.Lovers)}>♡</color>");
                }
                else if (__instance.Is(CustomRoles.Lovers) && PlayerControl.LocalPlayer.Data.IsDead)
                {
                    Mark.Append($"<color={Utils.GetRoleColorCode(CustomRoles.Lovers)}>♡</color>");
                }
                else if (__instance.Is(CustomRoles.Ntr) || PlayerControl.LocalPlayer.Is(CustomRoles.Ntr))
                {
                    Mark.Append($"<color={Utils.GetRoleColorCode(CustomRoles.Lovers)}>♡</color>");
                }
                else if (__instance == PlayerControl.LocalPlayer && CustomRoles.Ntr.Exist())
                {
                    Mark.Append($"<color={Utils.GetRoleColorCode(CustomRoles.Lovers)}>♡</color>");
                }

                //seerに関わらず発動するLowerText
                Suffix.Append(CustomRoleManager.GetLowerTextOthers(seer, target));

                //seer役職が対象のSuffix
                Suffix.Append(seerRole?.GetSuffix(seer, target));

                //seerに関わらず発動するSuffix
                Suffix.Append(CustomRoleManager.GetSuffixOthers(seer, target));

                if (Options.CurrentGameMode == CustomGameMode.SoloKombat)
                    Suffix.Append(SoloKombatManager.GetDisplayHealth(target));

                /*if(main.AmDebugger.Value && main.BlockKilling.TryGetValue(target.PlayerId, out var isBlocked)) {
                    Mark = isBlocked ? "(true)" : "(false)";
                }*/
                if ((Utils.IsActive(SystemTypes.Comms) && Options.CommsCamouflage.GetBool()) || Concealer.IsHidding)
                    RealName = $"<size=0>{RealName}</size> ";

                string DeathReason = seer.Data.IsDead && seer.KnowDeathReason(target) ? $"({Utils.ColorString(Utils.GetRoleColor(CustomRoles.Doctor), Utils.GetVitalText(target.PlayerId))})" : "";
                //Mark・Suffixの適用
                target.cosmetics.nameText.text = $"{RealName}{DeathReason}{Mark}";

                if (Suffix.ToString() != "")
                {
                    //名前が2行になると役職テキストを上にずらす必要がある
                    RoleText.transform.SetLocalY(0.35f);
                    target.cosmetics.nameText.text += "\r\n" + Suffix.ToString();

                }
                else
                {
                    //役職テキストの座標を初期値に戻す
                    RoleText.transform.SetLocalY(0.2f);
                }
            }
            else
            {
                //役職テキストの座標を初期値に戻す
                RoleText.transform.SetLocalY(0.2f);
            }
        }
    }
    //FIXME: 役職クラス化のタイミングで、このメソッドは移動予定
    public static void LoversSuicide(byte deathId = 0x7f, bool isExiled = false, bool now = false)
    {
        if (Options.LoverSuicide.GetBool() && CustomRoles.Lovers.Exist(true) && Main.isLoversDead == false)
        {
            foreach (var loversPlayer in Main.LoversPlayers)
            {
                //生きていて死ぬ予定でなければスキップ
                if (!loversPlayer.Data.IsDead && loversPlayer.PlayerId != deathId) continue;

                Main.isLoversDead = true;
                foreach (var partnerPlayer in Main.LoversPlayers)
                {
                    //本人ならスキップ
                    if (loversPlayer.PlayerId == partnerPlayer.PlayerId) continue;

                    //残った恋人を全て殺す(2人以上可)
                    //生きていて死ぬ予定もない場合は心中
                    if (partnerPlayer.PlayerId != deathId && !partnerPlayer.Data.IsDead)
                    {
                        PlayerState.GetByPlayerId(partnerPlayer.PlayerId).DeathReason = CustomDeathReason.FollowingSuicide;
                        if (isExiled)
                        {
                            if (now) partnerPlayer?.RpcExileV2();
                            MeetingHudPatch.TryAddAfterMeetingDeathPlayers(CustomDeathReason.FollowingSuicide, partnerPlayer.PlayerId);
                        }
                        else
                            partnerPlayer.RpcMurderPlayer(partnerPlayer);
                    }
                }
            }
        }
    }
}
[HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.Start))]
class PlayerStartPatch
{
    public static void Postfix(PlayerControl __instance)
    {
        var roleText = UnityEngine.Object.Instantiate(__instance.cosmetics.nameText);
        roleText.transform.SetParent(__instance.cosmetics.nameText.transform);
        roleText.transform.localPosition = new Vector3(0f, 0.2f, 0f);
        roleText.transform.localScale = new(1f, 1f, 1f);
        roleText.fontSize = Main.RoleTextSize;
        roleText.text = "RoleText";
        roleText.gameObject.name = "RoleText";
        roleText.enabled = false;
    }
}
[HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.SetColor))]
class SetColorPatch
{
    public static bool IsAntiGlitchDisabled = false;
    public static bool Prefix(PlayerControl __instance, int bodyColor)
    {
        //色変更バグ対策
        if (!AmongUsClient.Instance.AmHost || __instance.CurrentOutfit.ColorId == bodyColor || IsAntiGlitchDisabled) return true;
        return true;
    }
}
[HarmonyPatch(typeof(Vent), nameof(Vent.EnterVent))]
class EnterVentPatch
{
    public static void Postfix(Vent __instance, [HarmonyArgument(0)] PlayerControl pc)
    {
        Main.LastEnteredVent.Remove(pc.PlayerId);
        Main.LastEnteredVent.Add(pc.PlayerId, __instance);
        Main.LastEnteredVentLocation.Remove(pc.PlayerId);
        Main.LastEnteredVentLocation.Add(pc.PlayerId, pc.GetTruePosition());
    }
}
[HarmonyPatch(typeof(PlayerPhysics), nameof(PlayerPhysics.CoEnterVent))]
class CoEnterVentPatch
{
    public static bool Prefix(PlayerPhysics __instance, [HarmonyArgument(0)] int id)
    {
        if (!AmongUsClient.Instance.AmHost) return true;

        Logger.Info($"{__instance.myPlayer.GetNameWithRole()} CoEnterVent: {id}", "CoEnterVent");

        var user = __instance.myPlayer;

        if ((!user.GetRoleClass()?.OnEnterVent(__instance, id) ?? false) ||
                    (user.Data.Role.Role != RoleTypes.Engineer && //エンジニアでなく
                !user.CanUseImpostorVentButton()) //インポスターベントも使えない
        )
        {
            MessageWriter writer = AmongUsClient.Instance.StartRpcImmediately(__instance.NetId, (byte)RpcCalls.BootFromVent, SendOption.Reliable, -1);
            writer.WritePacked(127);
            AmongUsClient.Instance.FinishRpcImmediately(writer);
            new LateTask(() =>
            {
                int clientId = user.GetClientId();
                MessageWriter writer2 = AmongUsClient.Instance.StartRpcImmediately(__instance.NetId, (byte)RpcCalls.BootFromVent, SendOption.Reliable, clientId);
                writer2.Write(id);
                AmongUsClient.Instance.FinishRpcImmediately(writer2);
            }, 0.5f, "Fix DesyncImpostor Stuck");
            return false;
        }
        return true;
    }
}

[HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.SetName))]
class SetNamePatch
{
    public static void Postfix(PlayerControl __instance, [HarmonyArgument(0)] string name)
    {
    }
}
[HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.CompleteTask))]
class PlayerControlCompleteTaskPatch
{
    public static bool Prefix(PlayerControl __instance)
    {
        var pc = __instance;

        Logger.Info($"TaskComplete:{pc.GetNameWithRole()}", "CompleteTask");
        var taskState = pc.GetPlayerTaskState();
        taskState.Update(pc);

        var roleClass = pc.GetRoleClass();
        bool ret;
        if (roleClass != null && roleClass.OnCompleteTask(out bool cancel))
        {
            ret = cancel;
        }
        else
        {
            ret = Capitalism.OnCompleteTask(pc);
            if (ret) ret = Workhorse.OnCompleteTask(pc);
        }

        Utils.NotifyRoles();
        return ret;
    }
}
[HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.ProtectPlayer))]
class PlayerControlProtectPlayerPatch
{
    public static void Postfix(PlayerControl __instance, [HarmonyArgument(0)] PlayerControl target)
    {
        Logger.Info($"{__instance.GetNameWithRole()} => {target.GetNameWithRole()}", "ProtectPlayer");
    }
}
[HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.RemoveProtection))]
class PlayerControlRemoveProtectionPatch
{
    public static void Postfix(PlayerControl __instance)
    {
        Logger.Info($"{__instance.GetNameWithRole()}", "RemoveProtection");
    }
}
[HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.RpcSetRole))]
class PlayerControlSetRolePatch
{
    public static bool Prefix(PlayerControl __instance, ref RoleTypes roleType)
    {
        var target = __instance;
        var targetName = __instance.GetNameWithRole();
        Logger.Info($"{targetName} =>{roleType}", "PlayerControl.RpcSetRole");
        if (!ShipStatus.Instance.enabled) return true;
        if (roleType is RoleTypes.CrewmateGhost or RoleTypes.ImpostorGhost)
        {
            var targetRequireResetCam = target.GetCustomRole().GetRoleInfo()?.RequireResetCam ?? false;
            var targetIsKiller = target.Is(CustomRoleTypes.Impostor) || Main.ResetCamPlayerList.Contains(target.PlayerId) || targetRequireResetCam;
            var ghostRoles = new Dictionary<PlayerControl, RoleTypes>();
            foreach (var seer in Main.AllPlayerControls)
            {
                var self = seer.PlayerId == target.PlayerId;
                var seerRequireResetCam = seer.GetCustomRole().GetRoleInfo()?.RequireResetCam ?? false;
                var seerIsKiller = seer.Is(CustomRoleTypes.Impostor) || Main.ResetCamPlayerList.Contains(seer.PlayerId) || seerRequireResetCam;

                if ((self && targetIsKiller) || (!seerIsKiller && target.Is(CustomRoleTypes.Impostor)))
                {
                    ghostRoles[seer] = RoleTypes.ImpostorGhost;
                }
                else
                {
                    ghostRoles[seer] = RoleTypes.CrewmateGhost;
                }
            }
            if (ghostRoles.All(kvp => kvp.Value == RoleTypes.CrewmateGhost))
            {
                roleType = RoleTypes.CrewmateGhost;
            }
            else if (ghostRoles.All(kvp => kvp.Value == RoleTypes.ImpostorGhost))
            {
                roleType = RoleTypes.ImpostorGhost;
            }
            else
            {
                foreach ((var seer, var role) in ghostRoles)
                {
                    Logger.Info($"Desync {targetName} =>{role} for{seer.GetNameWithRole()}", "PlayerControl.RpcSetRole");
                    target.RpcSetRoleDesync(role, seer.GetClientId());
                }
                return false;
            }
        }
        return true;
    }
}