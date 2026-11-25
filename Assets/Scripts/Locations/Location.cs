using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Jedna lokacia v scene (farma, Explore...). Vsetky su v tej istej scene, len posunute od seba,
/// takze inventar, cas dna aj dorastanie surovin su spolocne.
///
/// Area je obdlznik (relativne k objektu), kde lokacia je. Kamera sa v nej drzi a podla nej sa zisti,
/// kde je hrac. Location.Current sa da pouzit napr. na "hrac nie je doma, ked pride noc" (P2-05).
///
/// Nastavuje to Baa-ricade > Vygenerovat mapu / Vygenerovat Explore lokaciu (na objektoch Environment a Explore).
/// </summary>
public class Location : MonoBehaviour
{
    private static readonly List<Location> all = new List<Location>();

    [Tooltip("Nazov, ktory sa ukaze pri prichode.")]
    [SerializeField] private string displayName = "Location";
    [Tooltip("Obdlznik lokacie relativne k tomuto objektu (aj hranice kamery).")]
    [SerializeField] private Rect area = new Rect(0, 0, 64, 32);

    /// <summary>Lokacia, v ktorej je hrac.</summary>
    public static Location Current { get; private set; }
    /// <summary>Hrac presiel do inej lokacie.</summary>
    public static event Action<Location> Changed;

    public string DisplayName => displayName;
    public Vector2 Min => (Vector2)transform.position + area.min;
    public Vector2 Max => (Vector2)transform.position + area.max;

    public bool Contains(Vector2 point) =>
        point.x >= Min.x && point.x <= Max.x && point.y >= Min.y && point.y <= Max.y;

    /// <summary>Lokacia, v ktorej je bod (null, ak v ziadnej).</summary>
    public static Location At(Vector2 point)
    {
        foreach (var location in all)
            if (location.Contains(point)) return location;
        return null;
    }

    void OnEnable() => all.Add(this);

    void OnDisable()
    {
        all.Remove(this);
        if (Current == this) Current = null;
    }

    void Start()
    {
        // Na zaciatku hry: lokacia, v ktorej hrac stoji, nastavi kameru.
        var player = GameObject.FindWithTag("Player");
        if (player != null && Contains(player.transform.position)) Enter();
    }

    /// <summary>Hrac je odteraz tu: kamera sa drzi v tejto lokacii.</summary>
    public void Enter()
    {
        Current = this;
        var cam = Camera.main;
        var follow = cam != null ? cam.GetComponent<CameraFollow>() : null;
        if (follow != null) follow.SetBounds(Min, Max);
        Changed?.Invoke(this);
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.85f, 0.3f);
        Vector2 min = Min, max = Max;
        Gizmos.DrawWireCube((min + max) / 2f, max - min);
    }
}
