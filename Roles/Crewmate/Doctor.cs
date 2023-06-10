using AmongUs.GameOptions;

using TOHE.Roles.Core;
using TOHE.Roles.Core.Interfaces;

namespace TOHE.Roles.Crewmate;
public sealed class Doctor : RoleBase, IDeathReasonSeeable
{
    public static readonly SimpleRoleInfo RoleInfo =
        new(
            typeof(Doctor),
            player => new Doctor(player),
            CustomRoles.Doctor,
            () => RoleTypes.Scientist,
            CustomRoleTypes.Crewmate,
            21100,
            SetupOptionItem,
            "doc",
            "#80ffdd"
        );
    public Doctor(PlayerControl player)
    : base(
        RoleInfo,
        player
    )
    {
        TaskCompletedBatteryCharge = OptionTaskCompletedBatteryCharge.GetFloat();
    }
    private static OptionItem OptionTaskCompletedBatteryCharge;
    enum OptionName
    {
        DoctorTaskCompletedBatteryCharge
    }
    private static float TaskCompletedBatteryCharge;
    private static void SetupOptionItem()
    {
        OptionTaskCompletedBatteryCharge = FloatOptionItem.Create(RoleInfo, 10, OptionName.DoctorTaskCompletedBatteryCharge, new(0f, 10f, 1f), 5f, false)
            .SetValueFormat(OptionFormat.Seconds);
    }
    public override void ApplyGameOptions(IGameOptions opt)
    {
        AURoleOptions.ScientistCooldown = 0f;
        AURoleOptions.ScientistBatteryCharge = TaskCompletedBatteryCharge;
    }
}