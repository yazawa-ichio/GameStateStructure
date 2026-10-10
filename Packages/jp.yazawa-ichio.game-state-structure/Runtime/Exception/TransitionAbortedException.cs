using System;

namespace GameStateStructure
{
	/// <summary>Entry による作り直しで打ち切られた遷移。遷移ロックは停止しない。</summary>
	public class TransitionAbortedException : OperationCanceledException
	{
		public TransitionAbortedException()
			: base("transition was aborted by Entry")
		{
		}
	}
}