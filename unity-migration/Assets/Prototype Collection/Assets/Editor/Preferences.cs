// Prototype Collection - Street Props
// Copyright (c) Amplify Creations, Lda <info@amplify.pt>

using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

namespace AmplifyCreations.PrototypeCollection.StreetProps
{
	public class Preferences
	{		
		private static readonly GUIContent StartUp = new GUIContent( "Show start screen on Unity launch" );
		public static readonly string PrefHashGUIDBase64 = "Kz/ZJqsj7kSIf9yCXV4UdA==";
		public static string PrefStartUp => PrefHashGUIDBase64 + "NYC";
		public static bool GlobalStartUp = true;
	}
}