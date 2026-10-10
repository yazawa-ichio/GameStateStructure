using System;

namespace GameStateStructure
{
	public enum StateEventType
	{
		Enter,
		Exit,
		ForceRelease,
		Error,
	}

	public readonly struct StateEvent
	{
		public StateEventType Type { get; }

		/// <summary>対象のステート。<see cref="StateEventType.Error"/> では null のことがある。</summary>
		public GameState State { get; }

		/// <summary><see cref="StateEventType.Exit"/> のときのみ意味を持つ。</summary>
		public PopReason Reason { get; }

		/// <summary><see cref="StateEventType.Error"/> のときのみ意味を持つ。</summary>
		public Exception Error { get; }

		public StateEvent(StateEventType type, GameState state, PopReason reason = default, Exception error = null)
		{
			Type = type;
			State = state;
			Reason = reason;
			Error = error;
		}

		public override string ToString()
		{
			var name = State?.GetType().Name ?? "(none)";
			return Type switch
			{
				StateEventType.Exit => $"{Type} {name} ({Reason})",
				StateEventType.Error => $"{Type} {name} : {Error?.Message}",
				_ => $"{Type} {name}",
			};
		}
	}

	/// <summary>ステートの生成・遷移・解放を観測する。</summary>
	public interface IStateObserver
	{
		void OnStateEvent(in StateEvent e);
	}
}