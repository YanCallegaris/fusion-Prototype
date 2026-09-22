using Fusion;
using Fusion.Addons.KCC;
using System;
using UnityEngine;

public enum Abilitymode : byte
{
    BreakBlock,
    Cage,
    Shove
}

public class Player : NetworkBehaviour
{
    [SerializeField] private MeshRenderer[] modelParts;
    [SerializeField] private LayerMask lagCompLayers;
    [SerializeField] private KCC kcc;
    [SerializeField] private KCCProcessor glideProcessor;
    [SerializeField] private Transform camTarget;
    [SerializeField] private AudioSource source;
    [SerializeField] private AudioClip shoveSound;
    [SerializeField] private Cage cagePrefab;
    [SerializeField] private float maxPitch = 85f;
    [SerializeField] private float lookSensitivy = 0.15f;
    [SerializeField] private Vector3 jumpImpulse = new(0f, 10f, 0f);
    [SerializeField] private float doubleJumpMultiplayer = .75f;
    [SerializeField] private float breakBlockCD = 1.25f;
    [SerializeField] private float cageCD = 10f;
    [SerializeField] private float shoveCD = 2f;
    [SerializeField] private float grappleCD = 2f;
    [SerializeField] private float glideCD = 20f;
    [SerializeField] private float doubleJumpCD = 5f;
    [SerializeField] private float shoveStrength = 20f;
    [SerializeField] private float grappleStrengh = 12f;
    [SerializeField] private float maxGlideTime = 2f;
    [SerializeField] public float AbillityRange { get; set; } = 25f;


    public float BreakCDFactor => (GrappleCD.RemainingTime(Runner) ?? 0f) / breakBlockCD;
    public float CageCDFactor => (GrappleCD.RemainingTime(Runner) ?? 0f) / cageCD;
    public float ShoveCDFactor => (GrappleCD.RemainingTime(Runner) ?? 0f) / shoveCD;
    public float GrappleCDFactor => (GrappleCD.RemainingTime(Runner) ?? 0f) / grappleCD;
    public float GlideCDFactor => (GlideCD.RemainingTime(Runner) ?? 0f) / glideCD;
    public float DoubleJumpCDFactor => (DoubleJumpCD.RemainingTime(Runner) ?? 0f) / doubleJumpCD;
    public double Score => Math.Round(transform.position.y, 1);
    public bool IsReady; // Server is the only one who cares about this
    private bool CanGlide => !kcc.Data.IsGrounded && GlideCharge > 0f && !IsCaged;
    public Abilitymode SelectedAbility { get; private set; }

    [Networked] public string Name { get; private set; }
    [Networked] public float GlideCharge { get; private set; }
    [Networked] public bool IsGliding { get; private set; }
    [Networked] public bool IsCaged { get; set; }
    [Networked] public TickTimer BreakBlockCD { get; private set; }
    [Networked] public TickTimer CageCD { get; private set; }
    [Networked] public TickTimer ShoveCD { get; private set; }
    [Networked] public TickTimer GrappleCD { get; private set; }
    [Networked] public TickTimer GlideCD { get; private set; }
    [Networked] private TickTimer DoubleJumpCD { get; set; }
    [Networked] private NetworkButtons PreviousButtons { get; set; }
    [Networked, OnChangedRender(nameof(Jumped))] private int JumpSync { get; set; }
    [Networked, OnChangedRender(nameof(Shoved))] private int ShoveSync { get; set; }


    private InputManager inputManager;
    private Vector2 baseLookRotation;
    private float glideDrain;

    public override void Spawned()
    {
        glideDrain = 1f / (maxGlideTime * Runner.TickRate);
        GlideCharge = 1f;
        if (HasInputAuthority)
        {
            foreach (MeshRenderer renderer in modelParts)
            {
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;
            }
            inputManager = Runner.GetComponent<InputManager>();
            inputManager.LocalPlayer = this;
            Name = PlayerPrefs.GetString("Photon.Menu.Username");
            RPC_PlayerName(Name);
            CameraFollow.Singleton.SetTarget(camTarget, this);
            UIManager.Singleton.LocalPlayer = this;
            kcc.Settings.ForcePredictedLookRotation = true;
        }
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        if (HasInputAuthority)
        {
            CameraFollow.Singleton.SetTarget(null, this);
            UIManager.Singleton.LocalPlayer = null;
        }
    }

    public override void FixedUpdateNetwork()
    {
        if (GetInput(out NetInput input))
        {
            SelectedAbility = input.AbilityMode;
            CheckGlide(input);
            CheckJump(input);
            kcc.AddLookRotation(input.LookDelta * lookSensitivy, -maxPitch, maxPitch);
            UpdateCamTarget();
            Vector3 lookDirection = camTarget.forward;

            if (input.Buttons.WasPressed(PreviousButtons, InputButton.Grapple))
                TryGrapple(camTarget.forward);

            if (IsGliding && !CanGlide)
                ToggleGlide(false);

            SetInputDirection(input);
            CheckAbilities(input, lookDirection);
            PreviousButtons = input.Buttons;
            baseLookRotation = kcc.GetLookRotation();
        }
    }

    private void CheckJump(NetInput input)
    {
        if (input.Buttons.WasPressed(PreviousButtons, InputButton.Jump))
        {
            if (kcc.FixedData.IsGrounded)
            {
                kcc.Jump(jumpImpulse);
                JumpSync++;
            }
            else if (DoubleJumpCD.ExpiredOrNotRunning(Runner))
            {
                kcc.Jump(jumpImpulse * doubleJumpMultiplayer);
                DoubleJumpCD = TickTimer.CreateFromSeconds(Runner, doubleJumpCD);
                ToggleGlide(false);
                JumpSync++;
            }
        }
    }

    private void SetInputDirection(NetInput input)
    {
        Vector3 worldDirection;
        if (IsGliding)
        {
            GlideCharge = Mathf.Max(0f, GlideCharge - glideDrain);
            worldDirection = kcc.Data.TransformDirection;
        }
        else
            worldDirection = kcc.FixedData.TransformRotation * input.Direction.X0Y();

        kcc.SetInputDirection(worldDirection);
    }

    public override void Render()
    {
        if (kcc.Settings.ForcePredictedLookRotation)
        {
            Vector2 predictedLookRotation = baseLookRotation + inputManager.AccumulatedMouseDelta * lookSensitivy;
            kcc.SetLookRotation(predictedLookRotation);
        }
        UpdateCamTarget();
    }

    private void CheckGlide(NetInput input)
    {
        if (input.Buttons.WasPressed(PreviousButtons, InputButton.Glide) && GlideCD.ExpiredOrNotRunning(Runner) && CanGlide)
            ToggleGlide(true);
        else if (input.Buttons.WasReleased(PreviousButtons, InputButton.Glide) && IsGliding)
            ToggleGlide(false);
    }

    private void UpdateCamTarget()
    {
        camTarget.localRotation = Quaternion.Euler(kcc.GetLookRotation().x, 0f, 0f);
    }

    private void CheckAbilities(NetInput input, Vector3 lookDirecion)
    {
        if (HasStateAuthority || !input.Buttons.WasPressed(PreviousButtons, InputButton.UseAbility))
            return;

        switch (input.AbilityMode)
        {
            case Abilitymode.BreakBlock:
                TryBreakBlock(lookDirecion);
                break;
            case Abilitymode.Cage:
                TryCage(lookDirecion);
                break;
            case Abilitymode.Shove:
                TryShove(lookDirecion);
                break;
            default:
                break;
        }
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.InputAuthority | RpcTargets.StateAuthority)]
    public void RPC_SetReady()
    {
        IsReady = true;
        if (HasInputAuthority)
            UIManager.Singleton.DidSetReady();
    }

    public void Teleport(Vector3 position, Quaternion rotation)
    {
        kcc.SetPosition(position);
        kcc.SetLookRotation(rotation);
    }

    public void ResetCooldowns()
    {
        BreakBlockCD = TickTimer.None;
        CageCD = TickTimer.None;
        ShoveCD = TickTimer.None;
        GrappleCD = TickTimer.None;
        GlideCD = TickTimer.None;
        DoubleJumpCD = TickTimer.None;
    }

    private void TryBreakBlock(Vector3 lookDirection)
    {
        if (BreakBlockCD.ExpiredOrNotRunning(Runner) && Physics.Raycast(camTarget.position, lookDirection, out RaycastHit hitInfo, AbillityRange))
        {
            if (hitInfo.collider.TryGetComponent(out Block block))
            {
                BreakBlockCD = TickTimer.CreateFromSeconds(Runner, breakBlockCD);
                block.Disable();
            }
        }
    }

    private void TryGrapple(Vector3 lookDirection)
    {
        if (GrappleCD.ExpiredOrNotRunning(Runner) && Physics.Raycast(camTarget.position, lookDirection, out RaycastHit hitInfo, AbillityRange))
        {
            if (hitInfo.collider.TryGetComponent(out Block _))
            {
                GrappleCD = TickTimer.CreateFromSeconds(Runner, grappleCD);
                Vector3 grappleVector = Vector3.Normalize(hitInfo.point - transform.position);
                if (grappleVector.y > 0f)
                    grappleVector = Vector3.Normalize(grappleVector + Vector3.up);

                kcc.Jump(grappleVector * grappleStrengh);
                ToggleGlide(false);
            }
        }
    }

    private void TryCage(Vector3 lookDirection)
    {
        if (CageCD.ExpiredOrNotRunning(Runner) && Runner.LagCompensation.Raycast(camTarget.position, lookDirection, AbillityRange, Object.InputAuthority, out LagCompensatedHit hit, lagCompLayers, HitOptions.IncludePhysX | HitOptions.IgnoreInputAuthority))
        {
            if (hit.Hitbox != null && hit.Hitbox.TryGetComponent(out Player other))
            {
                CageCD = TickTimer.CreateFromSeconds(Runner, cageCD);
                other.Cage();
            }
        }
    }

    private void TryShove(Vector3 lookDirection)
    {
        if (ShoveCD.ExpiredOrNotRunning(Runner) && Runner.LagCompensation.Raycast(camTarget.position, lookDirection, AbillityRange, Object.InputAuthority, out LagCompensatedHit hit, lagCompLayers, HitOptions.IncludePhysX | HitOptions.IgnoreInputAuthority))
        {
            if (hit.Hitbox != null && hit.Hitbox.TryGetComponent(out Player other))
            {
                ShoveCD = TickTimer.CreateFromSeconds(Runner, shoveCD);
                other.Shove(lookDirection, shoveStrength);
            }
        }
    }

    private void Cage()
    {
        Runner.Spawn(cagePrefab, transform.position, Quaternion.identity, Object.InputAuthority).Init(this);
        IsCaged = true;
        ToggleGlide(false);
    }

    private void Shove(Vector3 direction, float strenght)
    {
        kcc.AddExternalImpulse(direction * strenght);
        ShoveSync++;
    }

    private void ToggleGlide(bool isGliding)
    {
        if (IsGliding == isGliding)
            return;

        if (isGliding)
        {
            kcc.AddModifier(glideProcessor);
            Vector3 velocity = kcc.Data.DynamicVelocity;
            velocity.y *= 0.25f;
            kcc.SetDynamicVelocity(velocity);
        }
        else
        {
            kcc.RemoveModifier(glideProcessor);
            GlideCharge = 1f;
            GlideCD = TickTimer.CreateFromSeconds(Runner, glideCD);
        }

        IsGliding = isGliding;
    }

    private void Jumped()
    {
        source.Play();
    }

    private void Shoved()
    {
        source.PlayOneShot(shoveSound);
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    private void RPC_PlayerName(string name)
    {
        Name = name;
    }
}