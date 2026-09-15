using Fusion;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public enum GameState
{
    Waiting,
    Playing,
}

public class GameLogic : NetworkBehaviour, IPlayerJoined, IPlayerLeft
{
    [SerializeField] private NetworkPrefabRef playerPrefab;
    [SerializeField] private Transform spawnPoint;
    [SerializeField] private Transform spawnPointPivot;
    [Networked] private Player Winner { get; set; }
    [Networked] private GameState state { get; set; }
    [Networked, Capacity(12)] private NetworkDictionary<PlayerRef, Player> Players => default;

    public override void Spawned()
    {
        Winner = null;
        state = GameState.Waiting;
    }

    private void OnTriggerEnter(Collider other)
    {
        //Detected when a player enters the finish platform`s trigger collider
        if (Runner.IsServer && Winner == null && other.attachedRigidbody != null && other.attachedRigidbody.TryGetComponent(out Player player))
        {
            UnreadyAll();
            Winner = player;
            state = GameState.Waiting;
        }
    }

    public override void FixedUpdateNetwork()
    {
        if (Players.Count < 1)
        return;

        bool areAllReady = true;
        foreach (KeyValuePair<PlayerRef, Player> player in Players)
        {
            if (!player.Value.IsReady)
            {
                areAllReady = false;
                break;
            }
        }

        if (areAllReady)
        {
            Winner = null;
            state = GameState.Playing;
            PreparePlayers();
        }
    }

    private void PreparePlayers()
    {
        float spacingAngle = 360f / Players.Count;
        spawnPointPivot.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
        foreach (KeyValuePair<PlayerRef, Player> player in Players)
        {
            GetNextSpawnPoint(spacingAngle, out Vector3 position, out Quaternion rotation);
            player.Value.Teleport(position, rotation);
        }
    }

    private void UnreadyAll()
    {
        foreach(KeyValuePair<PlayerRef, Player> player in Players)
        {
            player.Value.IsReady = false;
        }
    }

    private void GetNextSpawnPoint(float spacingAngle, out Vector3 position,  out Quaternion rotation)
    {
        position = spawnPoint.position;
        rotation = spawnPoint.rotation;
        spawnPointPivot.Rotate(0f, spacingAngle, 0f);
    }

    public void PlayerJoined(PlayerRef player)
    {
        NetworkObject playerObject = Runner.Spawn(playerPrefab, Vector3.up, Quaternion.identity, player);
        Players.Add(player, playerObject.GetComponent<Player>());
    }

    public void PlayerLeft(PlayerRef player)
    {
        if (!HasStateAuthority)
            return;

        if (Players.TryGet(player, out Player playerBehaviour))
        {
            Players.Remove(player);
            Runner.Despawn(playerBehaviour.Object);
        }
    }
}
