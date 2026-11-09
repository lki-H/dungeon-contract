namespace DungeonContract {
 public static class DcV8Profile {
  public static bool Active=>DcBootstrap.Argument("-dc-runtime","v2")=="v8";
 }
}
