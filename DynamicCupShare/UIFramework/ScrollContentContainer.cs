using Oxide.Ext.Chaos.UIFramework;

namespace Oxide.Ext.Chaos.UIFramework;

public class ScrollContentContainer : BaseContainer
{
	private ScrollViewComponent m_ScrollViewComponent;

	public static ScrollContentContainer Create(string name, BaseContainer parent, ScrollViewComponent scrollViewComponent)
	{
		scrollViewComponent.ValidateContentTransform();
		ScrollContentContainer container = UIContainerPool.Get<ScrollContentContainer>();
		container.m_ScrollViewComponent = scrollViewComponent;
		string contentName = string.IsNullOrEmpty(name) ? "ScrollContent" : name + "___Content";
		container.Initialize(contentName, parent, scrollViewComponent.ContentTransform);
		container.Element.DummyComponent = true;
		return container;
	}

	protected override void Initialize(string name, BaseContainer parent, RectTransformComponent transform)
	{
		base.Initialize(name, parent, transform);
		RootContainer.m_Children.Add(this);
	}

	public override void OnEnterPool()
	{
		m_ScrollViewComponent = null;
		base.OnEnterPool();
	}
}
