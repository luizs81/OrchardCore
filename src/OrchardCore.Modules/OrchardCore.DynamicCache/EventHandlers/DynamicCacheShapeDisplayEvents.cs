using System.Collections.Generic;
using System.IO;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Html;
using OrchardCore.DisplayManagement.Implementation;
using OrchardCore.DynamicCache.Services;
using OrchardCore.Environment.Cache;

namespace OrchardCore.DynamicCache.EventHandlers
{
    /// <summary>
    /// Caches shapes in the default <see cref="IDynamicCacheService"/> implementation.
    /// It uses the shape's metadata cache context to define the cache parameters.
    /// </summary>
    public class DynamicCacheShapeDisplayEvents : IShapeDisplayEvents
    {
        private readonly HashSet<CacheContext> _cached = new HashSet<CacheContext>();
        private readonly HashSet<CacheContext> _openScopes = new HashSet<CacheContext>();

        private readonly IDynamicCacheService _dynamicCacheService;
        private readonly ICacheScopeManager _cacheScopeManager;

        public DynamicCacheShapeDisplayEvents(IDynamicCacheService dynamicCacheService, ICacheScopeManager cacheScopeManager)
        {
            _dynamicCacheService = dynamicCacheService;
            _cacheScopeManager = cacheScopeManager;
        }

        public async Task DisplayingAsync(ShapeDisplayContext context)
        {
            var debugMode = Configuration.IsDebugModeEnabled;

            // The shape has cache settings and no content yet
            if (context.ShapeMetadata.IsCached && context.ChildContent == null)
            {

                var cacheContext = context.ShapeMetadata.Cache();
                _cacheScopeManager.EnterScope(cacheContext);
                _openScopes.Add(cacheContext);

                var cachedContent = await _dynamicCacheService.GetCachedValueAsync(cacheContext);

                if (cachedContent != null)
                {
                    // The contents of this shape was found in the cache.
                    // Add the cacheContext to _cached so that we don't try to cache the content again in the DisplayedAsync method.
                    _cached.Add(cacheContext);
                    context.ChildContent = new HtmlString(cachedContent);
                }
                else if (debugMode)
                {
                    context.ShapeMetadata.Wrappers.Add("CachedShapeWrapper");
                }
            }
        }

        public async Task DisplayedAsync(ShapeDisplayContext context)
        {
            var cacheContext = context.ShapeMetadata.Cache();

            // If the shape is not configured to be cached, continue as usual
            if (cacheContext == null)
            {
                if (context.ChildContent != null)
                {
                    string content;

                    using (var sw = new StringWriter())
                    {
                        context.ChildContent.WriteTo(sw, HtmlEncoder.Default);
                        content = sw.ToString();
                    }
                    
                    context.ChildContent = new HtmlString(content);
                }
                else
                {
                    context.ChildContent = HtmlString.Empty;
                }

                return;
            }

            // If we have got this far, then this shape is configured to be cached.
            // We need to determine whether or not the ChildContent of this shape was retrieved from the cache by the DisplayingAsync method above, as opposed to generated by the View Engine.
            // ChildContent will be generated by the View Engine if it was not available in the cache when we rendered the shape.
            // In this instance, we need insert the ChildContent into the cache so that subsequent attempt to render this shape can take advantage of the cached content.

            // If the ChildContent was retrieved form the cache, then the Cache Context will be present in the _cached collection (see the DisplayingAsync method in this class).
            // So, if the cache context is not present in the _cached collection, we need to insert the ChildContent value into the cache:
            if (!_cached.Contains(cacheContext) && context.ChildContent != null)
            {
                using (var sw = new StringWriter())
                {
                    context.ChildContent.WriteTo(sw, HtmlEncoder.Default);
                    await _dynamicCacheService.SetCachedValueAsync(cacheContext, sw.ToString());
                }
            }
        }

        public Task DisplayingFinalizedAsync(ShapeDisplayContext context)
        {
            var cacheContext = context.ShapeMetadata.Cache();

            if (cacheContext != null && _openScopes.Contains(cacheContext))
            {
                _cacheScopeManager.ExitScope();
                _openScopes.Remove(cacheContext);
            }

            return Task.CompletedTask;
        }
    }
}
