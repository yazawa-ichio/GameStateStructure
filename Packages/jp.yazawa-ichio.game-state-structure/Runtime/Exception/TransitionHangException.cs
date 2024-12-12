using System;

namespace GameStateStructure
{
	public class TransitionHangException : Exception
	{
		public TransitionHangException() : base("Restart GameStateManager.Entry Run") { }

		public TransitionHangException(Exception error) : base("transition hang : " + error.Message, error) { }
	}
}