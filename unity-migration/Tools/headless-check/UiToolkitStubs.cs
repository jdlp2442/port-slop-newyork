// Minimal UI Toolkit stubs for the type-check harness. NOT part of the deliverable.
using System;
using System.Collections.Generic;

namespace UnityEngine
{
    public struct Rect
    {
        public float x, y, width, height;
        public Rect(float x, float y, float width, float height) { this.x = x; this.y = y; this.width = width; this.height = height; }
    }
}

namespace UnityEngine.UIElements
{
    public enum LengthUnit { Pixel, Percent, Auto }
    public enum DisplayStyle { Flex, None }
    public enum AngleUnit { Degree, Gradian, Radian, Turn }
    public enum TrickleDown { NoTrickleDown, TrickleDown }

    public struct Length
    {
        public float value;
        public LengthUnit unit;
        public Length(float value, LengthUnit unit) { this.value = value; this.unit = unit; }
        public static implicit operator Length(float v) => new Length(v, LengthUnit.Pixel);
    }

    public struct StyleLength
    {
        public Length value;
        public StyleLength(Length l) { value = l; }
        public static implicit operator StyleLength(Length l) => new StyleLength(l);
        public static implicit operator StyleLength(float f) => new StyleLength(f);
    }

    public struct StyleEnum<T>
    {
        public T value;
        public StyleEnum(T v) { value = v; }
        public static implicit operator StyleEnum<T>(T v) => new StyleEnum<T>(v);
    }

    public struct Angle { public Angle(float v, AngleUnit u) { } }
    public struct Rotate { public Rotate(Angle a) { } }
    public struct Scale { public Scale(Vector3 v) { } }
    public struct Translate
    {
        public Translate(Length x, Length y) { }
        public Translate(Length x, Length y, float z) { }
    }

    public struct Background { }
    public struct StyleBackground
    {
        public StyleBackground(Texture2D t) { }
    }

    public interface IStyle
    {
        StyleLength left { get; set; }
        StyleLength top { get; set; }
        StyleLength right { get; set; }
        StyleLength bottom { get; set; }
        StyleLength width { get; set; }
        StyleLength height { get; set; }
        StyleEnum<DisplayStyle> display { get; set; }
        StyleBackground backgroundImage { get; set; }
        Translate translate { get; set; }
        Scale scale { get; set; }
        Rotate rotate { get; set; }
    }

    public interface IResolvedStyle
    {
        float width { get; }
        float height { get; }
        DisplayStyle display { get; }
    }

    sealed class StyleImpl : IStyle
    {
        public StyleLength left { get; set; }
        public StyleLength top { get; set; }
        public StyleLength right { get; set; }
        public StyleLength bottom { get; set; }
        public StyleLength width { get; set; }
        public StyleLength height { get; set; }
        public StyleEnum<DisplayStyle> display { get; set; }
        public StyleBackground backgroundImage { get; set; }
        public Translate translate { get; set; }
        public Scale scale { get; set; }
        public Rotate rotate { get; set; }
    }

    sealed class ResolvedImpl : IResolvedStyle
    {
        public float width => 288f;
        public float height => 184f;
        public DisplayStyle display => DisplayStyle.Flex;
    }

    public abstract class EventBase<T> where T : EventBase<T>, new() { }
    public class PointerDownEvent : EventBase<PointerDownEvent> { public Vector3 position; public Vector3 localPosition; public int pointerId; }
    public class PointerMoveEvent : EventBase<PointerMoveEvent> { public Vector3 position; public Vector3 localPosition; public int pointerId; }
    public class PointerUpEvent : EventBase<PointerUpEvent> { public Vector3 position; public Vector3 localPosition; public int pointerId; }
    public class PointerCaptureOutEvent : EventBase<PointerCaptureOutEvent> { public int pointerId; }
    public class WheelEvent : EventBase<WheelEvent> { public Vector3 delta; public Vector3 localPosition; public Vector3 position; }
    public class ChangeEvent<T> : EventBase<ChangeEvent<T>> { public T newValue; public T previousValue; }

    public delegate void EventCallback<T>(T evt);

    public class VisualElement
    {
        public string name;
        public readonly IStyle style = new StyleImpl();
        public readonly IResolvedStyle resolvedStyle = new ResolvedImpl();
        public Rect layout;
        public VisualElement parent;

        public T Q<T>(string name = null) where T : VisualElement => null;
        public VisualElement Q(string name) => null;
        public void Add(VisualElement child) { }
        public void Clear() { }
        public void AddToClassList(string c) { }
        public void RemoveFromClassList(string c) { }
        public void EnableInClassList(string c, bool enable) { }
        public bool ClassListContains(string c) => false;
        public void CapturePointer(int pointerId) { }
        public void ReleasePointer(int pointerId) { }
        public bool HasPointerCapture(int pointerId) => false;
        public void RegisterCallback<T>(EventCallback<T> callback, TrickleDown useTrickleDown = TrickleDown.NoTrickleDown)
            where T : EventBase<T>, new() { }
    }

    public class Label : VisualElement
    {
        public string text;
    }

    public class Button : VisualElement
    {
        public Button() { }
        public Button(Action clickEvent) { }
        public event Action clicked;
        public string text;
    }

    public class TextField : VisualElement
    {
        public string value;
        public string label;
        public void RegisterValueChangedCallback(EventCallback<ChangeEvent<string>> callback) { }
    }

    public class UIDocument : MonoBehaviour
    {
        public VisualElement rootVisualElement;
    }
}
