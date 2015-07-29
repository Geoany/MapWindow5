﻿// -------------------------------------------------------------------------------------------
// <copyright file="GisToolBase.cs" company="MapWindow OSS Team - www.mapwindow.org">
//  MapWindow OSS Team - 2015
// </copyright>
// -------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Reflection;
using MW5.Api.Helpers;
using MW5.Api.Interfaces;
using MW5.Api.Static;
using MW5.Plugins.Concrete;
using MW5.Plugins.Interfaces;
using MW5.Plugins.Services;
using MW5.Shared;
using MW5.Tools.Model.Parameters;
using MW5.Tools.Views;

namespace MW5.Tools.Model
{
    /// <summary>
    /// Base class for GIS tool.
    /// </summary>
    public abstract class GisToolBase: IGisTool
    {
        private List<BaseParameter> _parameters;
        private ILayerService _layerService;
        private IMessageService _messageService;
        private IAppContext _context;

        /// <summary>
        /// Gets name of the tool.
        /// </summary>
        public abstract string Name { get; }

        /// <summary>
        /// Gets description of the tool.
        /// </summary>
        public abstract string Description { get; }

        /// <summary>
        /// Gets the identity of plugin that created this tool.
        /// </summary>
        public PluginIdentity PluginIdentity { get; private set; }

        protected IMessageService MessageService
        {
            get { return _messageService; }
        }

        protected ILayerService LayerService
        {
            get { return _layerService; }
        }

        protected IAppContext AppContext
        {
            get { return _context; }
        }

        /// <summary>
        /// Initializes the tool with application context.
        /// Can be overriden in derived classes to provide additional logic.
        /// </summary>
        public virtual void Initialize(IAppContext context)
        {
            if (context == null) throw new ArgumentNullException("context");
            _context = context;

            foreach (var layerParameter in Parameters.OfType<LayerParameterBase>())
            {
                layerParameter.Initialize(context.Layers);
            }

            _messageService = context.Container.Resolve<IMessageService>();
            _layerService = context.Container.Resolve<ILayerService>();
        }

        /// <summary>
        /// Must be overriden in derived classes to provide execution logic for the tool.
        /// </summary>
        public abstract bool Run();

        public bool Validate()
        {
            foreach (var p in Parameters)
            {
                var outputParameter = p as OutputLayerParameter;
                if (outputParameter != null)
                {
                    string errorMessage;
                    if (!outputParameter.Value.Validate(out errorMessage))
                    {
                        MessageService.Info(errorMessage);
                        return false;
                    }
                }

                var value = p as ValueParameter;
                if (value != null)
                {
                    string errorMessage;
                    if (!value.Validate(out errorMessage))
                    {
                        MessageService.Info(errorMessage);
                        return false;
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// Gets combined list of required and optional parameters.
        /// </summary>
        public IEnumerable<BaseParameter> Parameters
        {
            get { return _parameters ?? (_parameters = GetParameters().ToList()); }
        }

        private IEnumerable<BaseParameter> GetParameters()
        {
            var properties = GetType().GetProperties();
            foreach (var prop in properties)
            {
                if (!typeof(BaseParameter).IsAssignableFrom(prop.PropertyType))
                {
                    continue;
                }

                var attr = Attribute.GetCustomAttribute(prop, typeof(ParameterAttribute)) as ParameterAttribute;

                if (attr == null) continue;

                var param = Activator.CreateInstance(prop.PropertyType) as BaseParameter;
                if (param != null)
                {
                    prop.SetValue(this, param);
                    param.Name = prop.Name;
                    param.Index = attr.Index;
                    param.DisplayName = attr.DisplayName;
                    param.Required = attr is RequiredParameterAttribute;

                    HandleRangeAttribute(param, prop);

                    HandleDefaultValueAttribute(param, prop);

                    yield return param;
                }
            }
        }

        private void HandleDefaultValueAttribute(BaseParameter param, PropertyInfo prop)
        {
            var attr = prop.GetAttribute<DefaultValueAttribute>();
            if (attr != null)
            {
                param.SetDefaultValue(attr.Value);
            }
        }

        private void HandleRangeAttribute(BaseParameter param, PropertyInfo prop)
        {
            var vp = param as ValueParameter;
            if (vp != null && vp.Numeric)
            {
                var range = prop.GetAttribute<RangeAttribute>();
                if (param is IntegerParameter)
                {
                    (param as IntegerParameter).MinValue = (int)range.Minimum;
                    (param as IntegerParameter).MaxValue = (int)range.Maximum;
                }
                else if (param is DoubleParameter)
                {
                    (param as DoubleParameter).MinValue = (double)range.Minimum;
                    (param as DoubleParameter).MaxValue = (double)range.Maximum;
                }
            }
        }

        protected bool HandleOutput(IDatasource ds, OutputLayerInfo outputInfo)
        {
            if (outputInfo.MemoryLayer)
            {
                return HandleMemoryOutput(ds, outputInfo);
            }

            return HandleDiskOutput(ds, outputInfo);
        }

        private bool HandleDiskOutput(IDatasource ds, OutputLayerInfo outputInfo)
        {
            string filename = outputInfo.Name;

            if (File.Exists(filename) && !outputInfo.Overwrite)
            {
                return HandleOverwriteFailure();
            }

            if (outputInfo.Overwrite)
            {
                if (!GeoSource.Remove(filename))
                {
                    return HandleOverwriteFailure();
                }

                if (LayerSourceHelper.Save(ds, filename))
                {
                    Logger.Current.Info("Layer ({0}) with random points is created.", filename);
                }
                else
                {
                    Logger.Current.Error("Failed to save datasource: " + ds.LastError);
                    return false;
                }
            }

            ds.Dispose();

            if (outputInfo.AddToMap)
            {
                return LayerService.AddLayersFromFilename(filename);
            }

            return true;
        }

        private bool HandleMemoryOutput(IDatasource ds, OutputLayerInfo outputInfo)
        {
            if (outputInfo.AddToMap)
            {
                bool result = LayerService.AddDatasource(ds);
                if (result)
                {
                    int layerHandle = LayerService.LastLayerHandle;
                    var layer = AppContext.Layers.ItemByHandle(layerHandle);
                    if (layer != null)
                    {
                        layer.Name = outputInfo.Name;
                    }
                }

                return true;
            }

            ds.Dispose();
            Logger.Current.Warn("Memory layer created by the tool wasn't added to the map.");
            return false;
        }

        private bool HandleOverwriteFailure()
        {
            return false;
        }
    }
}