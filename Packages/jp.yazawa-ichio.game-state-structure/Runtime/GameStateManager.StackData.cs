using System.Collections.Generic;
using System.Threading;

namespace GameStateStructure
{

	public partial class GameStateManager
	{
		class StackData
		{
			public CancellationTokenSource CancellationTokenSource = new();

			public StackData Parent;

			public GameState State;

			public List<StackData> Children = new();

			public void Update()
			{
				State?.DoUpdate();
				foreach (var child in Children)
				{
					child.Update();
				}
			}

			public void PreCancel()
			{
				CancellationTokenSource?.Cancel();
				CancellationTokenSource = null;
				foreach (var child in Children)
				{
					child.PreCancel();
				}
			}
		}


	}

}