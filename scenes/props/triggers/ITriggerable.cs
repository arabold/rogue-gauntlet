/// <summary>
/// A receiver a trigger source reaches through its <see cref="TriggerOutputComponent"/>: a one-time effect on a pulse (a
/// shot, a spring), a lasting one while active (a stream of shots, spikes held up, a door held open).
/// </summary>
public interface ITriggerable
{
	void Pulse(in TriggerActivation activation);
	void SetActive(bool active, in TriggerActivation activation);
}