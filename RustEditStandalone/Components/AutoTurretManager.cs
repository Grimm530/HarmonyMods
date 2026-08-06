using UnityEngine;

namespace RustEditStandalone.Components;

public sealed class AutoTurretManager : MonoBehaviour
{
    private AutoTurret _turret;
    private int _ammoItemId;
    public bool UnlimitedAmmo { get; private set; }

    private void Awake()
    {
        _turret = GetComponent<AutoTurret>();
        enabled = false;
    }

    public void Setup(bool unlimitedAmmo, bool peaceKeeper, string weaponShortname)
    {
        UnlimitedAmmo = unlimitedAmmo;
        if (_turret == null) return;

        _turret.SetPeacekeepermode(peaceKeeper);

        if (!string.IsNullOrEmpty(weaponShortname))
        {
            Item item = ItemManager.CreateByName(weaponShortname, 1, 0uL);
            if (item != null)
            {
                item.RemoveFromContainer();
                item.RemoveFromWorld();
                item.position = 0;
                item.SetParent(_turret.inventory);
                _turret.inventory.MarkDirty();
                item.MarkDirty();
                _turret.Invoke(_turret.UpdateAttachedWeapon, 0.5f);
            }
        }

        if (UnlimitedAmmo)
            _turret.Invoke(EnsureAmmo, 1f);
    }

    private void EnsureAmmo()
    {
        if (_turret == null || !UnlimitedAmmo) return;
        if (_turret.AttachedWeapon == null)
        {
            _turret.Invoke(EnsureAmmo, 1f);
            return;
        }

        if (_ammoItemId == 0)
        {
            var weapon = _turret.GetAttachedWeapon();
            var def = weapon?.primaryMagazine?.ammoType;
            if (def != null) _ammoItemId = def.itemid;
        }

        if (_ammoItemId != 0 && _turret.inventory != null &&
            _turret.inventory.GetAmount(_ammoItemId, onlyUsableAmounts: true) < 1)
        {
            Item ammo = ItemManager.CreateByItemID(_ammoItemId, 128, 0uL);
            if (ammo != null)
            {
                _turret.inventory.GiveItem(ammo);
                _turret.inventory.MarkDirty();
            }
        }

        _turret.Invoke(EnsureAmmo, 1f);
    }

    public bool ShouldRefundAmmoUse() => UnlimitedAmmo;
}
