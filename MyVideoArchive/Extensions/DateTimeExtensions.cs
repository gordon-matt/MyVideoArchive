namespace MyVideoArchive.Extensions;

public static class DateTimeExtensions
{
    extension(DateTime? source)
    {
        public DateTime? AsUtc()
        {
            if (!source.HasValue)
            {
                return null;
            }

            var dt = source.Value;

            return dt.Kind switch
            {
                DateTimeKind.Utc => dt,
                DateTimeKind.Local => dt.ToUniversalTime(),
                DateTimeKind.Unspecified => DateTime.SpecifyKind(dt, DateTimeKind.Utc),
                _ => dt
            };
        }
    }
}