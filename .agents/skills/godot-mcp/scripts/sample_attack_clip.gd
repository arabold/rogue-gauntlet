extends SceneTree
# Prints a bone's height, forward reach and speed at 5% steps through one animation clip, to find where
# an attack's visible strike happens before authoring its ContactStart/ContactEnd.
#
# Usage (headless is fine; run from the project root):
#   scripts/godot.sh --headless --path "$PWD" --script <this file> -- \
#     <actor scene> <AnimationPlayer path from the scene root> <clip> <bone>
# Example:
#   ... -- res://scenes/enemies/skeleton/skeleton_minion.tscn Pivot/Skeleton_Minion/AnimationPlayer 1H_Melee_Attack_Chop wrist.r
# An unknown bone name prints the skeleton's bone list instead. The strike is the burst of high speed
# after the wind-up plateau; contact should open as it starts and close as it settles.

var actor: Node
var player: AnimationPlayer
var skeleton: Skeleton3D
var bone := -1
var clip := ""
var frames := 0

func _init() -> void:
	var args := OS.get_cmdline_user_args()
	if args.size() < 4:
		printerr("usage: -- <scene> <animation player path> <clip> <bone>")
		quit(1)
		return
	actor = load(args[0]).instantiate()
	root.add_child(actor)
	player = actor.get_node(args[1])
	clip = args[2]
	skeleton = actor.find_children("*", "Skeleton3D", true, false)[0]
	bone = skeleton.find_bone(args[3])

func _process(_delta: float) -> bool:
	frames += 1
	if frames < 3:
		return false  # let the scene finish entering the tree
	if bone < 0:
		var names := []
		for i in skeleton.get_bone_count():
			names.append(skeleton.get_bone_name(i))
		print("bones: ", names)
		quit()
		return true
	var animation := player.get_animation(clip)
	print("clip %s length %.3f s" % [clip, animation.length])
	player.play(clip)
	player.pause()
	var previous = null
	for step in range(21):
		var time := animation.length * step / 20.0
		player.seek(time, true)
		skeleton.force_update_all_bone_transforms()
		var position := skeleton.get_bone_global_pose(bone).origin
		var speed: float = 0.0 if previous == null else (position - previous).length() / (animation.length / 20.0)
		print("%3d%%  t=%.3f  height=%.3f  forward=%.3f  speed=%.2f" % [step * 5, time, position.y, position.z, speed])
		previous = position
	quit()
	return true
