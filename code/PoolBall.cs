﻿using System.Linq;
using System.Threading.Tasks;
using Sandbox;
using Sandbox.Diagnostics;
using Sandbox.Network;

namespace Facepunch.Pool;

public class PoolBall : Component, Component.ICollisionListener
{
	[Property] public PoolBallType Type { get; set; }
	[Property] public PoolBallNumber Number { get; set; }
	[Property] public Rigidbody Physics { get; set; }
	public PoolPlayer LastStriker { get; set; }
	public bool IsAnimating { get; private set; }
	
	private BallPocket LastPocket { get; set; }
	[Sync] private float RenderAlpha { get; set; }

	public void OnEnterPocket( BallPocket pocket )
	{
		Assert.True( Networking.IsHost );
		LastPocket = pocket;
		GameState.Instance.OnBallEnterPocket( this, pocket );
	}

	[Broadcast( NetPermission.HostOnly )]
	public void StartPlacing()
	{
		Physics.PhysicsBody.EnableSolidCollisions = false;
		Physics.MotionEnabled = false;
	}

	[Broadcast( NetPermission.HostOnly )]
	public void Respawn( Vector3 position )
	{
		RenderAlpha = 1f;
		Transform.Scale = 1f;
		Transform.Position = position;

		var renderer = Components.Get<ModelRenderer>();
		renderer.Tint = renderer.Tint.WithAlpha( RenderAlpha );

		Physics.AngularVelocity = Vector3.Zero;
		Physics.Velocity = Vector3.Zero;
		Physics.ClearForces();
	}

	public string GetIconClass()
	{
		return Type switch
		{
			PoolBallType.Black => "black",
			PoolBallType.White => "white",
			_ => $"{Type.ToString().ToLower()}_{(int)Number}"
		};
	}

	public bool CanPlayerHit( PoolPlayer player )
	{
		if ( player.BallType == PoolBallType.White )
		{
			return Type != PoolBallType.Black;
		}

		if ( GameState.Instance.GetBallPlayer( this ) == player )
			return true;

		return Type == PoolBallType.Black && player.BallsLeft == 0;
	}

	public void StartAnimating()
	{
		if ( IsAnimating ) return;
		Assert.True( Networking.IsHost );
		IsAnimating = true;
		DisableCollisions();
	}

	[Broadcast( NetPermission.HostOnly )]
	private void DisableCollisions()
	{
		Physics.PhysicsBody.EnableSolidCollisions = false;
		Physics.MotionEnabled = false;
	}

	[Broadcast( NetPermission.HostOnly )]
	private void EnableCollisions()
	{
		Physics.PhysicsBody.EnableSolidCollisions = true;
		Physics.MotionEnabled = true;
	}

	[Broadcast( NetPermission.HostOnly )]
	public void StopPlacing()
	{
		Physics.PhysicsBody.EnableSolidCollisions = true;
		Physics.MotionEnabled = true;
		Physics.AngularVelocity = Vector3.Zero;
		Physics.Velocity = Vector3.Zero;
		Physics.ClearForces();
	}
	
	[Broadcast]
	public void TryMoveTo( Vector3 position )
	{
		if ( !Networking.IsHost ) return;
		Transform.Position = position.WithZ( Transform.Position.z );
	}

	protected override void OnStart()
	{
		Physics.AngularDamping = 0.6f;
		Physics.LinearDamping = 0.6f;
		RenderAlpha = 1f;
		
		base.OnStart();
	}

	protected override void OnUpdate()
	{
		var renderer = Components.Get<ModelRenderer>();

		if ( renderer is not null )
		{
			renderer.MaterialGroup = GetMaterialGroup();
			renderer.Tint = renderer.Tint.WithAlpha( RenderAlpha );
		}

		if ( IsAnimating )
		{
			RenderAlpha = RenderAlpha.LerpTo( 0f, Time.Delta * 5f );
			
			if ( LastPocket != null && LastPocket.IsValid() )
				Transform.Position = Transform.Position.LerpTo( LastPocket.Transform.Position, Time.Delta * 16f );
			
			if ( RenderAlpha.AlmostEqual( 0f ) )
			{
				IsAnimating = false;
				EnableCollisions();	
			}
		}
	}
	
	private string GetMaterialGroup()
	{
		return Type switch
		{
			PoolBallType.Black => "8",
			PoolBallType.Spots => ((int)Number).ToString(),
			PoolBallType.Stripes => ((int)Number + 8).ToString(),
			_ => "default"
		};
	}

	void ICollisionListener.OnCollisionStart( Collision info )
	{
		if ( !Networking.IsHost ) return;
		
		var otherObject = info.Other.GameObject;
		var otherBall = otherObject.Components.GetInDescendantsOrSelf<PoolBall>();
		if ( !otherBall.IsValid() ) return;
		if ( !Physics.MotionEnabled ) return;
		if ( !otherBall.Physics.MotionEnabled ) return;

		LastStriker = GameState.Instance.CurrentPlayer;
		GameState.Instance.OnBallHitOtherBall( this, otherBall );

		PlayCollideSound( info.Contact.NormalSpeed );
	}

	void ICollisionListener.OnCollisionUpdate( Collision info )
	{
		
	}

	void ICollisionListener.OnCollisionStop( CollisionStop info )
	{
		
	}

	public bool IsBallPlacementValid( Vector3 possiblePosition = default )
	{
		if ( possiblePosition == default )
			possiblePosition = GameObject.Transform.Position;
	
		bool isPlacementValid = IsInBounds( possiblePosition );

		if ( GameState.Instance.RoundCount == 1 )
			isPlacementValid = IsInKitchen( possiblePosition );

		return isPlacementValid;
	}

	public bool IsInBounds( Vector3 possiblePosition = default )
	{
		if ( possiblePosition == default )
			possiblePosition = GameObject.Transform.Position;

		if ( possiblePosition.x >= 22 ||
			 possiblePosition.x <= -25 ||
			 possiblePosition.y >= 46 ||
			 possiblePosition.y <= -48 )
			return false;

		return true;
	}

	public bool IsInKitchen( Vector3 possiblePosition = default )
	{
		if ( possiblePosition == default )
			possiblePosition = GameObject.Transform.Position;

		if ( possiblePosition.y < 15 )
			return false;

		return true;
	}



	[Broadcast]
	public void PlayPocketSound()
	{
		Sound.Play( $"ball-pocket-{Game.Random.Int( 1, 2 )}" );
	}

	[Broadcast]
	private void PlayCollideSound( float speed )
	{
		var sound = Sound.Play( "ball-collide" );
		sound.Pitch = Game.Random.Float( 0.9f, 1f );
		sound.Volume = (1f / 100f) * speed;
	}
}
