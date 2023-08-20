using System;
using System.Text;

namespace GameStateStructure.Generator
{
	class CodeEmitter
	{
		class Scope : IDisposable
		{
			public Action OnDispose;
			public void Dispose()
			{
				OnDispose?.Invoke();
			}
		}

		StringBuilder m_Writer = new();
		int m_Indent = 0;

		void Tab()
		{
			for (int i = 0; i < m_Indent; i++)
			{
				m_Writer.Append("\t");
			}
		}

		public IDisposable Brace()
		{
			AppendLine("{");
			m_Indent++;
			return new Scope()
			{
				OnDispose = () =>
				{
					m_Indent--;
					AppendLine("}");
				}
			};
		}

		public void NewLine()
		{
			m_Writer.AppendLine();
		}

		public void AppendLine(string line)
		{
			Tab();
			m_Writer.AppendLine(line);
		}

		public void Clear()
		{
			m_Writer.Clear();
		}

		public override string ToString()
		{
			return m_Writer.ToString();
		}
	}

}