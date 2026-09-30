using System.ComponentModel.DataAnnotations;
using System.Reflection;
namespace PrivateBrandsPortal.Web.ViewModels;
public static class EnumLabel
{
    public static string Text<T>(T value) where T : struct, Enum => typeof(T).GetField(value.ToString())?.GetCustomAttribute<DisplayAttribute>()?.GetName() ?? value.ToString();
}
