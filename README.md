# Vapor Core API
- [Introduction](#introduction)
- [Installation](#installation)
- [Usage](#usage)
  - [Keys](#keys)
  - [Events](#events)
  - [Observable Values](#observable-values)
  - [UI Components](#state-machine)
  - [Inspector](#inspector)

## Introduction
A collection of tools for developing applications in Unity.

## Installation
- On the top right corner of this Github Page select the "Code" dropdown
- Copy the HTTPS link listed.
- In the Unity Editor open the Window -> Package Manager.
- Select the + in the top left and select "Add package from git URL".
- Paste the HTTPS link and select add.
- Updates can be managed in the Unity Package Manager from this point.

## Usage

### Keys
The backbone of many of the systems. A set of classes to create simple, stable, and unique integer key values.

#### How To Use

Most of the functionality is done by implementing the IKey interface on your own scriptable objects.
Optionally, you can inherit from KeySo or NamedKeySo.
Then manually generating the keys using the "Generate Keys" button in the inspector of those scriptable objects.

![image](https://github.com/Tyrant117/Vapor-SDK/assets/9998121/c0511af1-9856-408e-abb6-d8067b75c57a)

If custom implementing the IKey interface the user will need to call the formatter directly. See KeyGenerator.cs

- [KeySo](./Runtime/Keys/KeySo.cs): The base scriptable object implemention of the IKey interface.
- [KeyGenerator](./Runtime/Keys/KeyGenerator.cs): The main script that generates a Key Class File.

### Events
A global event system for tying events to the key system. 
Also contains expanded functionality for working with a provider-based component system, similar to dependency injection, to make sure data is where it needs to be when it's needed.

#### How To Use

The event system is broken into two parts. The EventBus and the ProviderBus.
The event bus can be thought of as a globally subscribable c# Action.
The provider bus can be thought of a globally subscribable c# Func.

The EventKeySo is used to map unique keys to the EventBus. These can be created with the Asset Create Menu -> Vapor -> Keys -> Event Key
The ProviderKeySo is used to map unique keys to the ProviderBus. These can be created with the Asset Create Menu -> Vapor -> Keys -> Provider Key

#### Provides Component Script
A MonoBehaviour that can be used to expose a component to a ProviderKey value.
- [ProvidesComponent](./Runtime/Events/Components/ProvidesComponent.cs)

![image](https://github.com/Tyrant117/Vapor-SDK/assets/9998121/e232d6c0-4527-443d-b1dc-0488c75f67da)


#### Helper Fields
There are three exposed helper fields to help the user link events in the inspector.
- [ChangedEventDataReceiver](./Runtime/Events/Fields/ChangedEventDataReceiver.cs): Receives data from a registered event key.
- [ChangedEventDataSender](./Runtime/Events/Fields/ChangedEventDataSender.cs): Sends data to all events regiestered to an event key.
- [RequestsProviderData](./Runtime/Events/Fields/RequestsProviderData.cs): Requests the result of a provider key.

![image](https://github.com/Tyrant117/Vapor-SDK/assets/9998121/d4b3b739-1f70-4db3-a572-355c2c9d998b)


### Observable Values
A wrapper on primitive types and some core Unity types that track when values are changed and optionally fires events when they are. 
Also contains a system for tying these values to a larger Observable Class to allow for grouped tracking of data.
They can also automatically be serialized to Json for easy save functionality.

#### How To Use
Usage is on a user desired basis. Where the user wants to have a tracked value replace the primitive value with its Observable.
```csharp
public class ExampleHealth : MonoBehaviour
{
    private const int HealthFieldID = 1;

    private FloatObservable _currentHealth;

    private void Awake()
    {
        _currentHealth = new FloatObservable(HealthFieldID, true, 100);
        _currentHealth.ValueChanged += CurrentHealthOnValueChanged;
    }

    private void CurrentHealthOnValueChanged(FloatObservable value, float oldValue)
    {
        Debug.Log($"Old Value: {oldValue} | New Value {value.Value}");
    }
}
```

### UI Components
A simple Mantine-like component library for UI Toolkit.

### Inspector
An Odin-like custom inspector system fully running in the new Unity UI Toolkit. The backbone of custom drawers for the rest of the SDK.

#### How To Use
- Decorate the MonoBehaviour you want to draw with custom attributes.
- With the script selected in the project go to Tools -> Vapor -> Inspector -> Create Inspectors From Selection.
- This will populate your local Vapor/Editor/Inspector folder with the custom drawer for the MonoBehaviour.
