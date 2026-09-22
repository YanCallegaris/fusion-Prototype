using Fusion;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.VisualScripting;
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
    [Networked, OnChangedRender(nameof(GameStateChanged))] private GameState State { get; set; }
    [Networked, Capacity(12)] private NetworkDictionary<PlayerRef, Player> Players => default;

    public override void Spawned()
    {
        Winner = null;
        State = GameState.Waiting;
        UIManager.Singleton.SetWaitUI(State, Winner);
        Runner.SetIsSimulated(Object, true);
    }

    private void OnTriggerEnter(Collider other)
    {
        //Detected when a player enters the finish platform`s trigger collider
        if (Runner.IsServer && Winner == null && other.attachedRigidbody != null && other.attachedRigidbody.TryGetComponent(out Player player))
        {
            UnreadyAll();
            Winner = player;
            State = GameState.Waiting;
        }
    }

    public override void FixedUpdateNetwork()
    {
        if (Players.Count < 1)
            return;

        if (Runner.IsServer && State == GameState.Waiting)
        {
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
                State = GameState.Playing;
                PreparePlayers();
            }
        }

        if(State == GameState.Playing && !Runner.IsResimulation)
        {
            UIManager.Singleton.UpdateLeaderboard(Players.OrderByDescending(p => p.Value).ToArray());
        }
    }

    private void GameStateChanged()
    {
        UIManager.Singleton.SetWaitUI(State, Winner);
    }

    private void PreparePlayers()
    {
        float spacingAngle = 360f / Players.Count;
        spawnPointPivot.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
        foreach (KeyValuePair<PlayerRef, Player> player in Players)
        {
            GetNextSpawnPoint(spacingAngle, out Vector3 position, out Quaternion rotation);
            player.Value.Teleport(position, rotation);
            player.Value.IsCaged = false;
            player.Value.ResetCooldowns();
        }
    }

    private void UnreadyAll()
    {
        foreach (KeyValuePair<PlayerRef, Player> player in Players)
        {
            player.Value.IsReady = false;
        }
    }

    private void GetNextSpawnPoint(float spacingAngle, out Vector3 position, out Quaternion rotation)
    {
        position = spawnPoint.position;
        rotation = spawnPoint.rotation;
        spawnPointPivot.Rotate(0f, spacingAngle, 0f);
    }

    public void PlayerJoined(PlayerRef player)
    {
        if (HasStateAuthority)
        {
            GetNextSpawnPoint(90f, out Vector3 position, out Quaternion rotation);
            NetworkObject playerObject = Runner.Spawn(playerPrefab, Vector3.up, Quaternion.identity, player);
            Players.Add(player, playerObject.GetComponent<Player>());
        }
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
