using UnityEngine;

namespace GameStateStructure.Logger
{
	public interface ILogHandler
	{
		void Log(LogLevel level, string tag, string message);
		void Log(Object context, LogLevel level, string tag, string message);
	}
}