namespace Topo.Model.AdditionalAwards
{
    // What a call that changes Terrain came back with. Terrain's rejections can carry no CORS header, so from a browser a
    // failed write can arrive as no readable reply at all (StatusCode 0).
    public class TerrainCallResult
    {
        public bool Success { get; set; }
        public int StatusCode { get; set; }
        public string Body { get; set; } = "";
    }
}
