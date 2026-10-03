/*++

Module Name:

    public.h

Abstract:

    This module contains the common declarations shared by driver
    and user applications.

Environment:

    user and kernel

--*/

//
// Define an Interface Guid so that apps can find the device and talk to it.
//

DEFINE_GUID (GUID_DEVINTERFACE_PalmRejFilter,
    0xe26b4470,0xb2e0,0x4be1,0x91,0x26,0x24,0x42,0xda,0xe2,0xb1,0x52);
// {e26b4470-b2e0-4be1-9126-2442dae2b152}
