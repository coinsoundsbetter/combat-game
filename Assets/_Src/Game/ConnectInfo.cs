namespace _Src.Game {
    public struct ConnectInfo {
        public int LocalPort;
        public string LocalAddress;
        public string TargetAddress;
        public int TargetPort;
        // In a remote match this peer controls Player 0 or Player 1.
        public int LocalPlayerIndex;
    }
}
