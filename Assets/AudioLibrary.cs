using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public static class AudioLibrary
{
    public static string Test = "event:/Test";
    public static string Dash = "event:/Player/Dash";
    public static string Jump = "event:/Player/Jump";

    // Blasters ----------------------------------------------------------------
    public enum WeaponSound { Weapon_A, PHOTON_RAVENGER, CHRONO_CANNON, DISRUPTOR, ION_FLUX, SONIC_CARBINE, NEUTRON_BLASTER };
    private static readonly Dictionary<WeaponSound, string> WeaponAudioLibrary = new Dictionary<WeaponSound, string>
    {
        { WeaponSound.Weapon_A,  "event:/Player/Lock"},
        { WeaponSound.PHOTON_RAVENGER, "event:/Blasters/Photon Ravenger" },
        { WeaponSound.CHRONO_CANNON, "event:/Blasters/Chrono Cannon"},
        { WeaponSound.DISRUPTOR, "event:/Blasters/Disruptor" },
        { WeaponSound.ION_FLUX, "event:/Blasters/Ion Flux"},
        { WeaponSound.SONIC_CARBINE, "event:/Blasters/Sonic Carbine"},
        { WeaponSound.NEUTRON_BLASTER, "event:/Blasters/Neutron Blaster"}
    };

    public static string GetWeaponSound(WeaponSound weapon)
    {
        return WeaponAudioLibrary[weapon];
    }


    // UI ----------------------------------------------------------------------



}
