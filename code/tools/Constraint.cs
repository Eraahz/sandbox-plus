﻿using System.Collections.Generic;
using System.Linq;
using System;
using Sandbox.UI;
using Sandbox.UI.Construct;
using Sandbox.Physics;

namespace Sandbox.Tools
{
	[Library( "tool_constraint", Title = "Constraint", Description = "Constrain stuff together", Group = "construction" )]
	public partial class ConstraintTool : BaseTool
	{
		// ConVar.ClientData doesn't seem to network its wrapped property nicely, so lets make our own...
		[ConVar.ClientData( "tool_constraint_type" )]
		public static ConstraintType _ { get; set; } = ConstraintType.Weld;
		private ConstraintType Type
		{
			get
			{
				var _ = Enum.TryParse( GetConvarValue( "tool_constraint_type" ), out ConstraintType val );
				return val;
			}
			set
			{
				ConsoleSystem.Run( "tool_constraint_type", value.ToString() );
			}
		}


		[Net, Predicted]
		private int stage { get; set; } = 0;
		private TraceResult trace1;
		private TraceResult trace2;
		private PhysicsJoint createdJoint;
		private Func<string> createdUndo;


		// Dynamic entrypoint for optional Wirebox support, if installed
		public static Action<Player, TraceResult, ConstraintType, PhysicsJoint, Func<string>> CreateWireboxConstraintController;
		private static bool WireboxSupport
		{
			get => CreateWireboxConstraintController != null;
		}

		public override void Simulate()
		{
			if ( Game.IsClient )
			{
				this.Description = CalculateDescription();

				if ( Input.Pressed( "drop" ) )
				{
					SelectNextType();
				}
			}

			using ( Prediction.Off() )
			{

				if ( !Game.IsServer )
					return;

				var tr = DoTrace();

				if ( !tr.Hit || !tr.Entity.IsValid() )
				{
					return;
				}


				if ( Input.Pressed( "attack1" ) )
				{
					if ( stage == 0 )
					{
						trace1 = tr;
						stage++;
					}
					else if ( stage == 1 )
					{
						trace2 = tr;
						if ( !trace1.Entity.IsValid() )
						{
							Reset();
							return;
						}
						if ( trace1.Entity.IsWorld && trace2.Entity.IsWorld )
						{
							return; // can't both be world
						}
						var point1 = PhysicsPoint.World( trace1.Body, trace1.EndPosition, Rotation.LookAt( -trace1.Normal, trace1.Direction ) );
						var point2 = PhysicsPoint.World( trace2.Body, trace2.EndPosition, Rotation.LookAt( trace2.Normal, trace2.Direction ) );

						if ( Type == ConstraintType.Weld )
						{
							var joint = PhysicsJoint.CreateFixed(
								point1,
								point2
							);
							joint.Collisions = true;
							trace1.Body.Sleeping = false;

							FinishConstraintCreation( joint, () =>
							{
								if ( joint.IsValid() )
								{
									joint.Remove();
									return $"Removed {Type} constraint";
								}
								return "";
							} );
						}
						else if ( Type == ConstraintType.Nocollide )
						{
							var joint = PhysicsJoint.CreateFixed(
								point1,
								point2
							);
							joint.EnableAngularConstraint = false;
							joint.EnableLinearConstraint = false;
							joint.Collisions = false;
							FinishConstraintCreation( joint, () =>
							{
								if ( joint.IsValid() )
								{
									joint.Remove();
									return $"Removed {Type} constraint";
								}
								return "";
							} );
						}
						else if ( Type == ConstraintType.Spring )
						{
							var length = trace1.EndPosition.Distance( trace2.EndPosition );
							var joint = PhysicsJoint.CreateSpring(
								point1,
								point2,
								length,
								length
							);
							joint.SpringLinear = new( 5.0f, 0.7f );
							joint.Collisions = true;
							joint.EnableAngularConstraint = false;

							var rope = MakeRope( trace1, trace2 );

							FinishConstraintCreation( joint, () =>
							{
								rope?.Destroy( true );
								if ( joint.IsValid() )
								{
									joint.Remove();
									return $"Removed {Type} constraint";
								}
								return "";
							} );
						}
						else if ( Type == ConstraintType.Rope )
						{
							var joint = PhysicsJoint.CreateLength(
								point1,
								point2,
								trace1.EndPosition.Distance( trace2.EndPosition )
							);
							joint.SpringLinear = new( 1000.0f, 0.7f );
							joint.Collisions = true;
							joint.EnableAngularConstraint = false;

							var rope = MakeRope( trace1, trace2 );

							FinishConstraintCreation( joint, () =>
							{
								rope?.Destroy( true );
								if ( joint.IsValid() )
								{
									joint.Remove();
									return $"Removed {Type} constraint";
								}
								return "";
							} );
						}
						else if ( Type == ConstraintType.Axis )
						{
							var pivot = Input.Down( "run" )
								? trace1.Body.MassCenter
								: trace1.EndPosition;

							var joint = PhysicsJoint.CreateHinge(
								trace1.Body,
								trace2.Body,
								pivot,
								trace1.Normal
							);
							joint.Collisions = true;
							trace1.Body.Sleeping = false;

							FinishConstraintCreation( joint, () =>
							{
								if ( joint.IsValid() )
								{
									joint.Remove();
									return $"Removed {Type} constraint";
								}
								return "";
							} );
						}
						else if ( Type == ConstraintType.BallSocket )
						{
							var pivot = Input.Down( "run" )
								? trace1.Body.MassCenter
								: trace1.EndPosition;

							var joint = PhysicsJoint.CreateBallSocket(
								trace1.Body,
								trace2.Body,
								pivot
							);
							joint.Collisions = true;
							trace1.Body.Sleeping = false;

							FinishConstraintCreation( joint, () =>
							{
								if ( joint.IsValid() )
								{
									joint.Remove();
									return $"Removed {Type} constraint";
								}
								return "";
							} );
						}
						else if ( Type == ConstraintType.Slider )
						{
							var joint = PhysicsJoint.CreateSlider(
								point1,
								point2,
								0,
								0 // can be used like a rope hybrid, to limit max length
							);
							var rope = MakeRope( trace1, trace2 );
							FinishConstraintCreation( joint, () =>
							{
								rope?.Destroy( true );
								if ( joint.IsValid() )
								{
									joint.Remove();
									return $"Removed {Type} constraint";
								}
								return "";
							} );
						}
					}
					else if ( stage == 2 )
					{
						// only reachable if Wirebox's installed
						if ( WireboxSupport )
						{
							CreateWireboxConstraintController( Owner, tr, Type, createdJoint, createdUndo );
						}
						Reset();
					}
				}
				else if ( Input.Pressed( "attack2" ) )
				{
					Reset();
				}
				else if ( Input.Pressed( "reload" ) )
				{
					if ( tr.Entity is not Prop prop )
					{
						return;
					}

					// todo: how to remove all constraints from X, where are they stored?

					Reset();
				}
				else
				{
					return;
				}

				CreateHitEffects( tr.EndPosition, tr.Normal );
			}
		}

		private void SelectNextType()
		{
			IEnumerable<ConstraintType> possibleEnums = Enum.GetValues<ConstraintType>();
			if ( Input.Down( "run" ) )
			{
				possibleEnums = possibleEnums.Reverse();
			}
			Type = possibleEnums.SkipWhile( e => e != Type ).Skip( 1 ).FirstOrDefault();
		}

		private string CalculateDescription()
		{
			var desc = $"Constraint entities together using a {Type} constraint";
			if ( Type == ConstraintType.Axis )
			{
				if ( stage == 0 )
				{
					desc += $"\nFirst, shoot the part that spins (eg. wheel).";
				}
				else if ( stage == 1 )
				{
					desc += $"\nSecond, shoot the base. Hold shift to use wheel's center of mass.";
				}
			}
			else
			{
				if ( stage == 1 )
				{
					desc += $"\nSecond, shoot the base.";
				}
			}
			if ( WireboxSupport )
			{
				if ( stage == 1 )
				{
					desc += $"\nHold alt to begin creating a Wire Constraint Controller";
				}
				else if ( stage == 2 )
				{
					desc += $"\nFinally, place the Wire Constraint Controller";
				}
			}
			return desc;
		}

		private void FinishConstraintCreation( PhysicsJoint joint, Func<string> undo )
		{
			joint.OnBreak += () => { undo(); };

			Event.Run( "undo.add", undo, Owner );

			if ( WireboxSupport && Input.Down( "walk" ) )
			{
				createdJoint = joint;
				createdUndo = undo;
				stage = 2;
				return;
			}
			Reset();
		}

		private static Particles MakeRope( TraceResult trace1, TraceResult trace2 )
		{
			var rope = Particles.Create( "particles/rope.vpcf" );

			if ( trace1.Body.GetEntity().IsWorld )
			{
				rope.SetPosition( 0, trace1.EndPosition );
			}
			else
			{
				rope.SetEntityBone( 0, trace1.Body.GetEntity(), trace1.Bone, new Transform( trace1.Body.GetEntity().Transform.PointToLocal( trace1.EndPosition ) ) );
			}
			if ( trace2.Body.GetEntity().IsWorld )
			{
				rope.SetPosition( 1, trace2.EndPosition );
			}
			else
			{
				rope.SetEntityBone( 1, trace2.Body.GetEntity(), trace2.Bone, new Transform( trace2.Body.GetEntity().Transform.PointToLocal( trace2.EndPosition ) ) );
			}
			return rope;
		}

		public override void CreateToolPanel()
		{
			if ( Game.IsClient )
			{
				var toolConfigUi = new ConstraintToolConfig();
				SpawnMenu.Instance?.ToolPanel?.AddChild( toolConfigUi );
			}
		}

		private void Reset()
		{
			stage = 0;
		}

		public override void Activate()
		{
			base.Activate();

			Reset();

		}

		public override void Deactivate()
		{
			base.Deactivate();

			Reset();
		}
	}

	public enum ConstraintType
	{
		Weld,
		Nocollide, // Generic
		Axis, // Revolute
		BallSocket, // Spherical
		Rope,
		Spring, // Winch/Hydraulic
		Slider, // Prismatic
	}

	[Library]
	public partial class ConstraintToolConfig : Panel
	{
		public ConstraintToolConfig()
		{
			StyleSheet.Load( "/ui/ConstraintTool.scss" );
			AddClass( "list" );
			List<Button> buttons = new();
			foreach ( var type in Enum.GetValues<ConstraintType>() )
			{
				var button = Add.Button( type.ToString(), "list_option" );
				button.AddEventListener( "onclick", () =>
				{
					ConsoleSystem.Run( "tool_constraint_type " + type.ToString() );
					foreach ( var child in buttons )
					{
						child.SetClass( "active", child == button );
					}
				} );
				button.SetClass( "active", type.ToString() == ConsoleSystem.GetValue( "tool_constraint_type", "Weld" ) );
				buttons.Add( button );
			}
		}
	}
}
