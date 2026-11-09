using UnityEngine;
namespace DungeonContract {
 public static class DcV7Profile {
  public static bool Active=>DcBootstrap.Argument("-dc-runtime","v2")=="v7"||DcV8Profile.Active;
 }
}
