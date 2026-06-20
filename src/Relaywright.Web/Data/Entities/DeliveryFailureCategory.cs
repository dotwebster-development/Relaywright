namespace Relaywright.Web.Data.Entities;

public enum DeliveryFailureCategory
{
    None = 0,
    Transient = 1,
    Permanent = 2,
    Configuration = 3,
    MessageFormat = 4
}
