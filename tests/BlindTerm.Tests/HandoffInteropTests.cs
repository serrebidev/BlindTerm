using System.Runtime.InteropServices;
using BlindTerm.App.Defterm;
using BlindTerm.Core.DefaultTerminal;

namespace BlindTerm.Tests;

/// <summary>
/// The COM side of being a default terminal, checked without a console.
///
/// These are unglamorous tests of things that cannot be seen by reading the code. .NET builds
/// a COM callable wrapper only for public types, so making a class or an interface internal --
/// an ordinary, tidy-looking edit -- silently reduces the object to a bare IUnknown. Windows
/// then declines the handoff with E_NOINTERFACE and says nothing, and the only visible symptom
/// is that command-line programs quietly go on opening in the old terminal. Each of these
/// asserts one thing that would fail exactly that way.
/// </summary>
public class HandoffInteropTests
{
    private static readonly Guid IID_IUnknown = new("00000000-0000-0000-C000-000000000046");
    private static readonly Guid IID_IClassFactory = new("00000001-0000-0000-C000-000000000046");
    private static readonly Guid IID_ITerminalHandoff3 = new("6F23DA90-15C5-4203-9DB0-64E73F1B1B00");

    /// <summary>Records what was posted to it instead of running it, so tests can look.</summary>
    private sealed class RecordingContext : SynchronizationContext
    {
        public List<Action> Posted { get; } = [];

        public override void Post(SendOrPostCallback callback, object? state)
            => Posted.Add(() => callback(state));

        public void Drain()
        {
            foreach (Action action in Posted.ToArray()) action();
            Posted.Clear();
        }
    }

    [Fact]
    public void TheHandoffObjectAnswersTheInterfaceWindowsAsksFor()
    {
        AssertExposes(new TerminalHandoff(new RecordingContext(), _ => { }), IID_ITerminalHandoff3);
    }

    [Fact]
    public void TheClassFactoryAnswersIClassFactory()
    {
        AssertExposes(new HandoffClassFactory(new RecordingContext(), _ => { }), IID_IClassFactory);
    }

    [Fact]
    public void TheFactoryHandsOutSomethingWindowsCanUse()
    {
        var factory = new HandoffClassFactory(new RecordingContext(), _ => { });
        Guid riid = IID_ITerminalHandoff3;

        int hr = factory.CreateInstance(IntPtr.Zero, ref riid, out IntPtr instance);

        Assert.Equal(0, hr);
        Assert.NotEqual(IntPtr.Zero, instance);
        Marshal.Release(instance);
    }

    [Fact]
    public void TheFactoryRefusesAggregation()
    {
        var factory = new HandoffClassFactory(new RecordingContext(), _ => { });
        Guid riid = IID_ITerminalHandoff3;

        // CLASS_E_NOAGGREGATION. Answering anything else to an outer unknown would be a lie
        // about supporting a COM feature this object does not implement.
        int hr = factory.CreateInstance(new IntPtr(1), ref riid, out IntPtr instance);

        Assert.Equal(unchecked((int)0x80040110), hr);
        Assert.Equal(IntPtr.Zero, instance);
    }

    [Fact]
    public void TheFactoryRefusesInterfacesItDoesNotHave()
    {
        var factory = new HandoffClassFactory(new RecordingContext(), _ => { });
        Guid riid = new("11111111-2222-3333-4444-555555555555");

        int hr = factory.CreateInstance(IntPtr.Zero, ref riid, out IntPtr instance);

        Assert.Equal(unchecked((int)0x80004002), hr);
        Assert.Equal(IntPtr.Zero, instance);
    }

    [Fact]
    public void LockServerSucceedsBothWays()
    {
        var factory = new HandoffClassFactory(new RecordingContext(), _ => { });

        Assert.Equal(0, factory.LockServer(true));
        Assert.Equal(0, factory.LockServer(false));
    }

    [Fact]
    public void TheInterfaceIsDeclaredTheWayWindowsDeclaresIt()
    {
        Type type = typeof(ITerminalHandoff3);

        Assert.Equal(IID_ITerminalHandoff3, type.GUID);
        Assert.True(type.IsPublic, "A non-public interface gets no COM callable wrapper.");
        Assert.True(Marshal.IsTypeVisibleFromCom(type));

        // One method, so its slot in the vtable is fixed and cannot drift.
        Assert.Single(type.GetMethods());
        Assert.Equal("EstablishPtyHandoff", type.GetMethods()[0].Name);

        var attribute = (InterfaceTypeAttribute?)Attribute.GetCustomAttribute(type, typeof(InterfaceTypeAttribute));
        Assert.Equal(ComInterfaceType.InterfaceIsIUnknown, attribute?.Value);
    }

    [Fact]
    public void TheComVisibleClassesAreVisibleToCom()
    {
        Assert.True(Marshal.IsTypeVisibleFromCom(typeof(TerminalHandoff)));
        Assert.True(Marshal.IsTypeVisibleFromCom(typeof(HandoffClassFactory)));
        Assert.True(Marshal.IsTypeVisibleFromCom(typeof(IClassFactory)));
    }

    [Fact]
    public void HandoffObjectsRequireAThreadToBuildTheWindowOn()
    {
        // Not a formality. Windows delivers this call on an RPC worker, where there is no
        // ambient context and no message loop; a window built there is created, titled, and
        // then never hears from anyone again.
        var context = new RecordingContext();

        Assert.Throws<ArgumentNullException>(() => new TerminalHandoff(null!, _ => { }));
        Assert.Throws<ArgumentNullException>(() => new TerminalHandoff(context, null!));
        Assert.Throws<ArgumentNullException>(() => new HandoffClassFactory(null!, _ => { }));
        Assert.Throws<ArgumentNullException>(() => new HandoffClassFactory(context, null!));
    }

    [Fact]
    public void TheClassIdMatchesTheOneWrittenToTheRegistry()
    {
        // The registry value and the class object have to name the same thing, or Windows
        // starts BlindTerm and then finds nothing registered under the class it wanted.
        Assert.Equal("{A5CDF81F-9E27-4F5D-A63A-FBA21DCB8BDD}",
            RegistrationScope.Format(DefaultTerminalConfig.BlindTermTerminal));
    }

    private static void AssertExposes(object instance, Guid iid)
    {
        IntPtr unknown = Marshal.GetIUnknownForObject(instance);
        try
        {
            Assert.NotEqual(IntPtr.Zero, unknown);

            int hr = Marshal.QueryInterface(unknown, in iid, out IntPtr found);
            Assert.Equal(0, hr);
            Assert.NotEqual(IntPtr.Zero, found);
            Marshal.Release(found);

            Guid unknownIid = IID_IUnknown;
            Assert.Equal(0, Marshal.QueryInterface(unknown, in unknownIid, out IntPtr asUnknown));
            Marshal.Release(asUnknown);
        }
        finally
        {
            Marshal.Release(unknown);
        }
    }
}
