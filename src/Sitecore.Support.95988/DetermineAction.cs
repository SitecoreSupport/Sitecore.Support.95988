using Sitecore;
using Sitecore.Collections;
using Sitecore.Configuration;
using Sitecore.Data;
using Sitecore.Data.Fields;
using Sitecore.Data.Items;
using Sitecore.Data.Templates;
using Sitecore.Diagnostics;
using Sitecore.Publishing;
using Sitecore.Publishing.Pipelines.PublishItem;
using Sitecore.Text;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Sitecore.StringExtensions;

namespace Sitecore.Support.Publishing.Pipelines.PublishItem
{
    public class DetermineAction : PublishItemProcessor
    {
        private class EvaluationResult<T>
        {
            // Fields
            private bool hasValue;
            private T value;

            // Methods
            [Conditional("DEBUG_PUBLISHING")]
            public void SetReason(string newReason)
            {
            }

            public void SetValue(T newValue)
            {
                this.value = newValue;
                this.hasValue = true;
            }

            // Properties
            public bool HasValue
            {
                get
                {
                    return this.hasValue;
                }
            }

            public T Value
            {
                get
                {
                    return this.value;
                }
            }
        }
        // Methods
        private Item GetSourceItem(PublishItemContext context)
        {
            Assert.ArgumentNotNull(context, "context");
            return context.PublishHelper.GetItemToPublish(context.ItemId);
        }

        private Item GetSourceVersion(Item sourceItem, PublishItemContext context)
        {
            Assert.ArgumentNotNull(sourceItem, "sourceItem");
            Assert.ArgumentNotNull(context, "context");
            return context.PublishHelper.GetVersionToPublish(sourceItem, context.PublishOptions.TargetDatabase);
        }

        private void HandleSourceItemNotFound(PublishItemContext context)
        {
            context.Action = PublishAction.DeleteTargetItem;
        }

        private void HandleSourceVersionNotFound(Item sourceItem, PublishItemContext context)
        {
            Func<Item, bool> predicate = null;
            Assert.ArgumentNotNull(sourceItem, "sourceItem");
            Item targetItem = context.PublishHelper.GetTargetItem(sourceItem.ID);
            if (targetItem == null)
            {
                if (Settings.Publishing.PublishEmptyItems)
                {
                    context.Action = PublishAction.PublishSharedFields;
                }
                else
                {
                    context.AbortPipeline(PublishOperation.Skipped, PublishChildAction.Skip, "No publishable source version exists (and there is no target item).");
                }
                return;
            }
            Item[] versions = targetItem.Versions.GetVersions(true);
            if (versions.Length > 0)
            {
                if (predicate == null)
                {
                    predicate = v => v.Language != sourceItem.Language;
                }
                if (versions.Any<Item>(predicate))
                {
                    if (this.CompareSharedFields(sourceItem, targetItem))
                    {
                        context.AbortPipeline(PublishOperation.Skipped, PublishChildAction.Skip, "No versions to publish in '{0}' language.".FormatWith(sourceItem.Language));
                        return;
                    }
                    context.Action = PublishAction.PublishSharedFields;
                    return;
                }
            }
            if (!Settings.Publishing.PublishEmptyItems)
            {
                context.Action = PublishAction.DeleteTargetItem;
                return;
            }
        }

        private bool MatchesPublishingTargets(Item item, PublishItemContext context)
        {
            string str = item[FieldIDs.PublishingTargets];
            if (string.IsNullOrEmpty(str))
            {
                return true;
            }
            ListString str2 = new ListString(str);
            foreach (string str3 in context.PublishOptions.PublishingTargets)
            {
                foreach (string str4 in str2)
                {
                    if (string.Equals(str3, str4, StringComparison.InvariantCulture))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        public override void Process(PublishItemContext context)
        {
            Assert.ArgumentNotNull(context, "context");
            if (context.Action == PublishAction.None)
            {
                Item sourceItem = this.GetSourceItem(context);
                if ((sourceItem == null) || !this.MatchesPublishingTargets(sourceItem, context))
                {
                    this.HandleSourceItemNotFound(context);
                }
                else
                {
                    Item sourceVersion = this.GetSourceVersion(sourceItem, context);
                    if (sourceVersion == null)
                    {
                        this.HandleSourceVersionNotFound(sourceItem, context);
                    }
                    else
                    {
                        context.Action = PublishAction.PublishVersion;
                        context.VersionToPublish = sourceVersion;
                    }
                }
            }
        }
        private bool CompareSharedFields(Item sourceItem, Item targetItem)
        {
            Assert.ArgumentNotNull(sourceItem, "sourceItem");
            Assert.ArgumentNotNull(targetItem, "targetItem");
            FieldCollection fields = sourceItem.Fields;
            FieldCollection fields2 = targetItem.Fields;
            EvaluationResult<bool> result = new EvaluationResult<bool>();
            foreach (Field field in fields)
            {
                TemplateField definition = field.Definition;
                if (((definition != null) && definition.IsShared) && (field.GetValue(false, false) != fields2[field.ID].GetValue(false, false)))
                {
                    result.SetValue(false);
                    break;
                }
            }
            if (!result.HasValue)
            {
                int count1 = fields.Where(f => f.Shared).Count();
                int count2 = fields2.Where(f => f.Shared).Count();
                if (count1 != count2)
                {
                    if (sourceItem.IsItemClone)
                    {
                        EvaluationResult<bool> result2 = this.CompareClonedFields(fields, fields2);
                        result.SetValue(result2.Value);
                    }
                    else
                    {
                        result.SetValue(false);
                    }
                }
                else
                {
                    result.SetValue(true);
                }
            }
            return result.Value;
        }
        private EvaluationResult<bool> CompareClonedFields(FieldCollection fields1, FieldCollection fields2)
        {
            Assert.ArgumentNotNull(fields1, "fields1");
            Assert.ArgumentNotNull(fields2, "fields2");
            EvaluationResult<bool> result = new EvaluationResult<bool>();
            IEnumerable<ID> first = from f in fields1 select f.ID;
            IEnumerable<ID> second = from f in fields2 select f.ID;
            IEnumerable<ID> enumerable3 = first.Except<ID>(second).Union<ID>(second.Except<ID>(first));
            bool newValue = true;
            foreach (ID id in enumerable3)
            {
                if (fields1[id].Value != fields2[id].Value)
                {
                    newValue = false;
                    break;
                }
            }
            result.SetValue(newValue);
            return result;
        }
    }
}
