namespace Request.Body.Peeker.BenchMark
{
    public class DummyClass
    {
        public string Name { get; set; }
        public string SurName { get; set; }

        public int CompareTo(DummyClass other)
        {
            if (Name.Equals(other.Name) && SurName.Equals(other.SurName))
                return 0;

            return -1;
        }
    }
}