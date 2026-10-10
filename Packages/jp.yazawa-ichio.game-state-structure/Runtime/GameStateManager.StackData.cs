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

			/// <summary>入っている車線。null は主軸。</summary>
			public System.Type Branch;

			/// <summary>IStateFactory が生成時に返した値。ライブラリは中身を解釈しない。</summary>
			public System.IDisposable FactoryValue;

			public GameState Exiting;

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