## A powerful animation system for Godot 4.x with group management, builders, and sequences.
##
## TweenerComponent provides a flexible, high-level interface for creating and managing
## animations using Godot's Tween system. It offers three API levels:
##
## 1. Simple API - Quick animations with animate_to()
## 2. Builder API - Complex animations with tween()
## 3. Sequence API - Choreographed multi-step animations with sequence()
##
## Features:
## - Group-based animation management (pause/resume/kill groups)
## - Global and per-group speed control
## - Custom curve support for advanced easing
## - Builder pattern for fluent animation creation
## - Sequence system for multi-step choreography
## - Automatic memory management and cleanup
## - Object pooling for TweenerData to reduce allocations
class_name TweenerComponent

enum UpdateType { IDLE, PHYSICS }

const DEFAULT_GROUP: String = "Generic"
const MAX_POOL_SIZE: int = 100

var _owner: Node

var _curves: Dictionary[String, Curve] = {}
var _tween_groups: Dictionary[String, Array] = {}
var _group_speeds: Dictionary[String, float] = {}

# Object pool for TweenerData
var _data_pool: Array[TweenerData] = []
var _pool_size: int = 0

var _update_type: UpdateType = UpdateType.IDLE
var default_transition: Tween.TransitionType
var default_ease: Tween.EaseType
var speed_scale: float = 1.0

func _init(owner: Node, default_trans: Tween.TransitionType = Tween.TRANS_CUBIC, default_ease_type: Tween.EaseType = Tween.EASE_IN) -> void:
	_owner = owner
	default_transition = default_trans
	default_ease = default_ease_type
	_prewarm_pool(10)  # Start with 10 pooled objects

# ==========================================
# OBJECT POOL MANAGEMENT
# ==========================================

## Prewarm the object pool with a specified number of TweenerData instances.
## Call this during initialization to avoid allocations during gameplay.
## [param count]: Number of objects to preallocate
func _prewarm_pool(count: int) -> void:
	for i in range(count):
		if _pool_size >= MAX_POOL_SIZE:
			break
		_data_pool.append(TweenerData.new())
		_pool_size += 1

## Get a TweenerData instance from the pool, or create a new one if pool is empty.
func _acquire_data() -> TweenerData:
	if _pool_size > 0:
		_pool_size -= 1
		var data = _data_pool.pop_back()
		data.reset()  # Clear previous values
		return data
	else:
		# Pool empty, create new instance
		return TweenerData.new()

## Return a TweenerData instance to the pool for reuse.
## [param data]: The TweenerData to return to pool
func _release_data(data: TweenerData) -> void:
	if _pool_size >= MAX_POOL_SIZE:
		return  # Pool is full, let it be garbage collected
	
	data.reset()
	_data_pool.append(data)
	_pool_size += 1

## Get current pool statistics for debugging.
## Returns Dictionary with "size", "capacity", and "utilization" keys
func get_pool_stats() -> Dictionary:
	return {
		"size": _pool_size,
		"capacity": MAX_POOL_SIZE,
		"utilization": float(MAX_POOL_SIZE - _pool_size) / MAX_POOL_SIZE
	}

# ==========================================
# ORIGINAL API (with pooling integration)
# ==========================================

func set_default_transition(transition: Tween.TransitionType, ease_type: Tween.EaseType) -> void:
	default_transition = transition
	default_ease = ease_type

func set_update(type: UpdateType) -> void:
	_update_type = type

func add_curve(key: String, curve: Curve) -> void:
	if _curves.has(key):
		push_warning("There is already a curve with key: %s" % key)
		return
	_curves[key] = curve

func remove_curve(key: String) -> bool:
	if _curves.has(key):
		_curves.erase(key)
		return true
	return false

func set_group_speed(group: String, speed: float) -> void:
	_group_speeds[group] = speed

func tween(target: Object, property: String) -> TweenBuilder:
	var builder: TweenBuilder = TweenBuilder.new(self, target, property)
	return builder

func sequence(target: Object, group: String = DEFAULT_GROUP) -> TweenSequence:
	var tween_sequence: TweenSequence = TweenSequence.new(self, target, group)
	return tween_sequence

func animate_to(target: Object, property: String, value: Variant, duration: float = 1.0, group: String = DEFAULT_GROUP) -> void:
	animate(target, property, [value], [duration], default_transition, default_ease, Callable(), group)

func animate(target: Object, property: String, values: Array, durations: PackedFloat32Array, 
	transition: Tween.TransitionType = Tween.TRANS_CUBIC, ease_type: Tween.EaseType = Tween.EASE_IN, 
	callback: Callable = Callable(), group: String = DEFAULT_GROUP, delay: float = 0.0) -> void:
	
	var final_transition = transition if transition != Tween.TRANS_CUBIC else default_transition
	var final_ease = ease_type if ease_type != Tween.EASE_IN else default_ease
	
	# Use pooled data instead of creating new instance
	var tweener_data: TweenerData = _acquire_data()
	tweener_data.group = group
	tweener_data.property = property
	tweener_data.values = values
	tweener_data.durations = durations
	tweener_data.transition = final_transition
	tweener_data.ease_type = final_ease
	tweener_data.delay_on_start = delay
	tweener_data.callback = callback
	
	_animate_internal_data(target, tweener_data)

func animate_with_callback(target: Object, property: String, values: Array, durations: PackedFloat32Array, callback: Callable) -> void:
	animate(target, property, values, durations, default_transition, default_ease, callback)

func tween_curve(target: Object, property: String, curve_key: String, from: Variant, to: Variant, duration: float = 1.0, callback: Callable = Callable(), group: String = DEFAULT_GROUP) -> Tween:
	if !is_instance_valid(target):
		push_error("Target is not instance valid")
		return null
	
	if !_curves.has(curve_key):
		push_error("Curve with key: %s, does not exist" % curve_key)
		return null
	
	var mode: Tween.TweenProcessMode = Tween.TWEEN_PROCESS_IDLE \
		if _update_type == UpdateType.IDLE else Tween.TWEEN_PROCESS_PHYSICS
	var new_tween: Tween = _owner.create_tween().set_process_mode(mode)
	
	var curve: Curve = _curves[curve_key]
	var group_speed: float = _group_speeds.get(group, 1.0)
	var final_speed: float = group_speed * speed_scale
	
	new_tween.set_speed_scale(final_speed)
	_add_tween_to_group(group, new_tween)
	
	new_tween.tween_method(_interpolate.bind(target, property, curve, from, to), 0.0, 1.0, duration / final_speed)
	new_tween.finished.connect(_check_tweener_callback.bind(new_tween, group, callback))
	
	return new_tween

## Execute animation from TweenerData (internal use, exposed for advanced scenarios).
## [param target]: The object to animate
## [param data]: TweenerData containing all animation parameters
func animate_data(target: Object, data: TweenerData) -> void:
	_animate_internal_data(target, data, false)

func _animate_internal_data(target: Object, data: TweenerData, internal: bool = true) -> void:
	if _check_errors(target, data.values, data.durations):
		if internal:
			_release_data(data)  # Return to pool on error
		return
	
	if data.start_call.is_valid():
		data.start_call.call()
	
	var mode: Tween.TweenProcessMode = Tween.TWEEN_PROCESS_IDLE \
		if _update_type == UpdateType.IDLE else Tween.TWEEN_PROCESS_PHYSICS
	var new_tween: Tween = _owner.create_tween().set_process_mode(mode)
	var count: int = min(data.values.size(), data.durations.size())
	
	if data.delay_on_start > 0.0:
		new_tween.tween_interval(data.delay_on_start)
	new_tween.set_trans(data.transition).set_ease(data.ease_type)
	new_tween.set_loops(data.loops)
	new_tween.set_parallel(data.parallel)
	
	_add_tween_to_group(data.group, new_tween)
	
	var group_speed: float = _group_speeds.get(data.group, 1.0)
	var final_speed: float = group_speed * speed_scale
	new_tween.set_speed_scale(final_speed)
	
	for i: int in range(count):
		new_tween.tween_property(target, data.property, data.values[i], data.durations[i])
	# Modified callback to release data back to pool
	new_tween.finished.connect(_check_tweener_callback_pooled.bind(new_tween, data.group, data.callback, data, internal))

# ==========================================
# GROUP MANAGEMENT
# ==========================================

func await_group_finish(group: String) -> void:
	while is_animating(group):
		await _owner.get_tree().process_frame

func has_group(group: String) -> bool:
	return _tween_groups.has(group)

func kill_group(group: String) -> void:
	if !_tween_groups.has(group):
		push_warning("Group with name: %s, does not exist" % group)
		return
	
	for t: Tween in _tween_groups[group]:
		if is_instance_valid(t):
			t.kill()
	_tween_groups[group].clear()

func kill_all() -> void:
	for group: String in _tween_groups.keys():
		kill_group(group)

func pause_all() -> void:
	for group: String in _tween_groups.keys():
		pause_group(group)

func resume_all() -> void:
	for group: String in _tween_groups.keys():
		resume_group(group)

func pause_group(group: String) -> void:
	_set_group_active(group, false)

func resume_group(group: String) -> void:
	_set_group_active(group, true)

func is_animating(group: String) -> bool:
	if !_tween_groups.has(group):
		return false
	
	var valid_count: int = 0
	for t: Tween in _tween_groups[group]:
		if is_instance_valid(t) && t.is_running():
			valid_count += 1
	return valid_count > 0

func clean_up() -> void:
	kill_all()
	_curves.clear()
	_tween_groups.clear()
	_group_speeds.clear()
	# Clear the pool
	_data_pool.clear()
	_pool_size = 0

func get_active_groups() -> Array[String]:
	return _tween_groups.keys()

func get_group_tweens(group: String) -> Array[Tween]:
	if !_tween_groups.has(group):
		return []
	return _tween_groups[group].duplicate()

# ==========================================
# INTERNAL METHODS
# ==========================================

func _set_group_active(group: String, active: bool) -> void:
	if !_tween_groups.has(group):
		push_error("Group with name: %s, does not exist" % group)
		return
	for t: Tween in _tween_groups[group]:
		if is_instance_valid(t):
			if active: t.play()
			else: t.pause()

func _add_tween_to_group(group: String, target_tween: Tween) -> void:
	if _tween_groups.has(group):
		_tween_groups[group].append(target_tween)
	else:
		_tween_groups[group] = [target_tween]

func _interpolate(t: float, target: Object, property: String, curve: Curve, a: Variant, b: Variant) -> void:
	var sample: float = curve.sample(t)
	target.set(property, lerp(a, b, sample))

func _check_tweener_callback(target_tween: Tween, group: String, callback: Callable) -> void:
	if callback.is_valid():
		callback.call()
	_tween_groups[group].erase(target_tween)
	if _tween_groups[group].is_empty():
		_tween_groups.erase(group)

## Modified callback that returns TweenerData to pool after completion
func _check_tweener_callback_pooled(target_tween: Tween, group: String, callback: Callable, data: TweenerData, should_release: bool) -> void:
	if callback.is_valid():
		callback.call()
	_tween_groups[group].erase(target_tween)
	if _tween_groups[group].is_empty():
		_tween_groups.erase(group)
	
	# Return data to pool if it was acquired from pool
	if should_release:
		_release_data(data)

func _check_errors(target: Object, values: Array, durations: PackedFloat32Array) -> bool:
	if !is_instance_valid(target):
		push_error("Target of this tween is not instance valid")
		return true
	
	if values.is_empty() || durations.is_empty():
		push_error("Can not pass an empty array to the tween")
		return true
	
	if values.size() != durations.size():
		push_warning("Values and Durations Size is different, some animations will not be shown")
	return false

# ==========================================
# BUILDER CLASS
# ==========================================

class TweenBuilder:
	var _component: TweenerComponent
	var _data: TweenerData
	var _target: Object
	
	func _init(component: TweenerComponent, target: Object, property: String) -> void:
		_component = component
		_target = target
		# Use pooled data
		_data = component._acquire_data()
		_data.property = property
		_data.transition = component.default_transition
		_data.ease_type = component.default_ease
		_data.group = TweenerComponent.DEFAULT_GROUP
	
	func to(...value: Array) -> TweenBuilder:
		_data.values = value
		return self
	
	func durations(...value: Array) -> TweenBuilder:
		_data.durations = PackedFloat32Array(value)
		return self
	
	func transition(tween_transition: Tween.TransitionType) -> TweenBuilder:
		_data.transition = tween_transition
		return self
	
	func ease_type(type: Tween.EaseType) -> TweenBuilder:
		_data.ease_type = type
		return self
	
	func delay(value: float) -> TweenBuilder:
		_data.delay_on_start = value
		return self
	
	func loops(value: int) -> TweenBuilder:
		_data.loops = value
		return self
	
	func parallel() -> TweenBuilder:
		_data.parallel = true
		return self
	
	func group(group_name: String) -> TweenBuilder:
		_data.group = group_name
		return self
	
	func on_start(method: Callable) -> TweenBuilder:
		_data.start_call = method
		return self
	
	func on_complete(callback: Callable) -> TweenBuilder:
		_data.callback = callback
		return self
	
	func start() -> void:
		if _data.values.is_empty():
			push_error("TweenBuilder: No values specified. Use .to() before .start()")
			_component._release_data(_data)  # Return to pool on error
			return
		
		if _data.durations.is_empty():
			var count = _data.values.size()
			_data.durations = PackedFloat32Array()
			for i in range(count):
				_data.durations.append(1.0)
		_component._animate_internal_data(_target, _data)
	
	# Convenience shortcuts
	func smooth() -> TweenBuilder:
		ease_in_out().transition(Tween.TRANS_SINE)
		return self
	
	func sine() -> TweenBuilder:
		transition(Tween.TRANS_SINE)
		return self
	
	func bounce() -> TweenBuilder:
		_data.transition = Tween.TRANS_BOUNCE
		return self

	func elastic() -> TweenBuilder:
		_data.transition = Tween.TRANS_ELASTIC
		return self

	func spring() -> TweenBuilder:
		_data.transition = Tween.TRANS_SPRING
		return self

	func linear() -> TweenBuilder:
		_data.transition = Tween.TRANS_LINEAR
		return self

	func ease_in() -> TweenBuilder:
		_data.ease_type = Tween.EASE_IN
		return self

	func ease_out() -> TweenBuilder:
		_data.ease_type = Tween.EASE_OUT
		return self

	func ease_in_out() -> TweenBuilder:
		_data.ease_type = Tween.EASE_IN_OUT
		return self

# ==========================================
# SEQUENCE CLASS
# ==========================================

class TweenSequence:
	var _component: TweenerComponent
	var _target: Object
	var _group: String
	var _steps: Array[SequenceStep] = []
	
	func _init(component: TweenerComponent, target: Object, group: String):
		_component = component
		_target = target
		_group = group
	
	func then(property: String, value: Variant, duration: float = 1.0) -> TweenSequence:
		_steps.append(SequenceStep.animate(property, value, duration, _component.default_transition, _component.default_ease))
		return self
	
	func wait(duration: float) -> TweenSequence:
		_steps.append(SequenceStep.wait_step(duration))
		return self
	
	func then_call(callback: Callable) -> TweenSequence:
		_steps.append(SequenceStep.callback_step(callback))
		return self
	
	func with_transition(trans: Tween.TransitionType) -> TweenSequence:
		if !_steps.is_empty():
			_steps[-1].transition_type = trans
		return self
	
	func with_ease(ease_type: Tween.EaseType) -> TweenSequence:
		if !_steps.is_empty():
			_steps[-1].ease_type = ease_type
		return self
	
	func start() -> void:
		_execute_next_step(0)
	
	func _execute_next_step(index: int) -> void:
		if index >= _steps.size():
			return
		
		var step: SequenceStep = _steps[index]
		
		match step.type:
			SequenceStep.StepType.Animate:
				_component.animate(
					_target,
					step.property,
					[step.value],
					[step.duration],
					step.transition_type,
					step.ease_type,
					func(): _execute_next_step(index + 1),
					_group
				)
			
			SequenceStep.StepType.Wait:
				await _component._owner.get_tree().create_timer(step.duration).timeout
				_execute_next_step(index + 1)
			
			SequenceStep.StepType.Callback:
				step.callback.call()
				_execute_next_step(index + 1)
	
	# Convenience shortcuts
	func bounce() -> TweenSequence:
		with_transition(Tween.TRANS_BOUNCE)
		return self
	
	func elastic() -> TweenSequence:
		with_transition(Tween.TRANS_ELASTIC)
		return self
	
	func spring() -> TweenSequence:
		with_transition(Tween.TRANS_SPRING)
		return self
	
	func ease_in() -> TweenSequence:
		with_ease(Tween.EASE_IN)
		return self
	
	func ease_out() -> TweenSequence:
		with_ease(Tween.EASE_OUT)
		return self

class SequenceStep:
	enum StepType { Animate, Wait, Callback }
	
	var type: StepType
	var property: String
	var value: Variant
	var duration: float = 1.0
	var transition_type: Tween.TransitionType
	var ease_type: Tween.EaseType
	var callback: Callable
	
	static func animate(prop: String, val: Variant, dur: float, trans: Tween.TransitionType, eas: Tween.EaseType) -> SequenceStep:
		var step: SequenceStep = SequenceStep.new()
		step.type = StepType.Animate
		step.property = prop
		step.value = val
		step.duration = dur
		step.transition_type = trans
		step.ease_type = eas
		
		return step
	
	static func wait_step(dur: float) -> SequenceStep:
		var step: SequenceStep = SequenceStep.new()
		StepType.Wait
		step.duration = dur
		return step
	
	static func callback_step(method: Callable) -> SequenceStep:
		var step: SequenceStep = SequenceStep.new()
		step.type = StepType.Callback
		step.callback = method
		return step
























