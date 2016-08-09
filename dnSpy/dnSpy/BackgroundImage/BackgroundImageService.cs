﻿/*
    Copyright (C) 2014-2016 de4dot@gmail.com

    This file is part of dnSpy

    dnSpy is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    dnSpy is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with dnSpy.  If not, see <http://www.gnu.org/licenses/>.
*/

using System;
using System.ComponentModel.Composition;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using dnSpy.Contracts.BackgroundImage;
using dnSpy.Contracts.Text.Editor;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;

namespace dnSpy.BackgroundImage {
	sealed class BackgroundImageService : IImageSourceServiceListener {
		readonly IWpfTextView wpfTextView;
		readonly IImageSourceService imageSourceService;
		IAdornmentLayer adornmentLayer;
		Image currentImage;

#pragma warning disable 0169
		[Export(typeof(AdornmentLayerDefinition))]
		[Name(PredefinedDnSpyAdornmentLayers.BackgroundImage)]
		[Order(Before = PredefinedDnSpyAdornmentLayers.BottomLayer)]
		[Order(Before = PredefinedDnSpyAdornmentLayers.TopLayer)]
		static AdornmentLayerDefinition backgroundImageAdornmentLayerDefinition;
#pragma warning restore 0169

		BackgroundImageService(IWpfTextView wpfTextView, IImageSourceService imageSourceService) {
			if (wpfTextView == null)
				throw new ArgumentNullException(nameof(wpfTextView));
			if (imageSourceService == null)
				throw new ArgumentNullException(nameof(imageSourceService));
			this.wpfTextView = wpfTextView;
			this.imageSourceService = imageSourceService;
			wpfTextView.Closed += WpfTextView_Closed;
			imageSourceService.Register(this);
		}

		void WpfTextView_LayoutChanged(object sender, TextViewLayoutChangedEventArgs e) {
			if (e.HorizontalTranslation || e.VerticalTranslation)
				UpdateImagePosition();
			else if (e.OldViewState.ViewportWidth != e.NewViewState.ViewportWidth)
				UpdateImagePosition();
			else if (e.OldViewState.ViewportHeight != e.NewViewState.ViewportHeight)
				UpdateImagePosition();
		}

		public static void InstallService(IWpfTextView wpfTextView, IImageSourceService imageSourceService) {
			if (wpfTextView == null)
				throw new ArgumentNullException(nameof(wpfTextView));
			if (imageSourceService == null)
				throw new ArgumentNullException(nameof(imageSourceService));
			wpfTextView.Properties.GetOrCreateSingletonProperty(typeof(BackgroundImageService), () => new BackgroundImageService(wpfTextView, imageSourceService));
		}

		public void OnEnabled() {
			if (adornmentLayer == null)
				adornmentLayer = wpfTextView.GetAdornmentLayer(PredefinedDnSpyAdornmentLayers.BackgroundImage);
			wpfTextView.LayoutChanged += WpfTextView_LayoutChanged;
		}

		public void OnDisabled() {
			wpfTextView.LayoutChanged -= WpfTextView_LayoutChanged;
			if (adornmentLayer != null)
				adornmentLayer.RemoveAllAdornments();
			currentImage = null;
		}

		static Stretch Filter(Stretch value) {
			if (value < 0 || value > Stretch.UniformToFill)
				return DefaultRawSettings.DefaultStretch;
			return value;
		}

		static StretchDirection Filter(StretchDirection value) {
			if (value < 0 || value > StretchDirection.Both)
				return DefaultRawSettings.DefaultStretchDirection;
			return value;
		}

		static double FilterOpacity(double value) {
			if (double.IsNaN(value))
				return DefaultRawSettings.Opacity;
			if (value < 0 || value > 1)
				return DefaultRawSettings.Opacity;
			return value;
		}

		static double FilterLength(double value) {
			if (double.IsNaN(value))
				return double.PositiveInfinity;
			if (value <= 0)
				return double.PositiveInfinity;
			return value;
		}

		static double FilterZoom(double value) {
			if (double.IsNaN(value))
				return DefaultRawSettings.Zoom;
			return value;
		}

		static double FilterOffset(double value) {
			if (double.IsNaN(value))
				return 0;
			return value;
		}

		static double FilterMargin(double value) {
			if (double.IsNaN(value))
				return 0;
			if (value < 0)
				return 0;
			return value;
		}

		Image InitializeImage(Image image) {
			image.Stretch = Filter(imageSourceService.Stretch);
			image.StretchDirection = Filter(imageSourceService.StretchDirection);
			image.Opacity = FilterOpacity(imageSourceService.Opacity);
			image.Source = imageSourceService.ImageSource;
			image.MaxHeight = FilterLength(imageSourceService.MaxHeight);
			image.MaxWidth = FilterLength(imageSourceService.MaxWidth);
			image.ClearValue(FrameworkElement.HeightProperty);
			image.ClearValue(FrameworkElement.WidthProperty);
			double scale = FilterZoom(imageSourceService.Zoom) / 100;
			if (scale == 1)
				image.LayoutTransform = Transform.Identity;
			else {
				var scaleTransform = new ScaleTransform(scale, scale);
				scaleTransform.Freeze();
				image.LayoutTransform = scaleTransform;
			}
			UpdateImagePosition(image);
			return image;
		}

		public void OnImageChanged() {
			if (currentImage == null) {
				currentImage = InitializeImage(new Image());
				AddImageToAdornmentLayer();
			}
			else
				InitializeImage(currentImage);
		}

		void UpdateImagePosition() {
			Debug.Assert(currentImage != null);
			if (currentImage == null)
				return;
			UpdateImagePosition(currentImage);
		}

		const bool resizeTooBigImages = true;
		void UpdateImagePosition(Image image) {
			double leftMargin = FilterMargin(imageSourceService.LeftMarginWidthPercent) / 100;
			double rightMargin = FilterMargin(imageSourceService.RightMarginWidthPercent) / 100;
			double topMargin = FilterMargin(imageSourceService.TopMarginHeightPercent) / 100;
			double bottomMargin = FilterMargin(imageSourceService.BottomMarginHeightPercent) / 100;
			if (double.IsNaN(leftMargin) || double.IsNaN(rightMargin) || leftMargin < 0 || rightMargin < 0 || leftMargin + rightMargin > 1) {
				leftMargin = 0;
				rightMargin = 0;
			}
			if (double.IsNaN(topMargin) || double.IsNaN(bottomMargin) || topMargin < 0 || bottomMargin < 0 || topMargin + bottomMargin > 1) {
				topMargin = 0;
				bottomMargin = 0;
			}
			double viewportWidth = (1 - leftMargin - rightMargin) * wpfTextView.ViewportWidth;
			double viewportHeight = (1 - topMargin - bottomMargin) * wpfTextView.ViewportHeight;
			double xOffs = leftMargin * wpfTextView.ViewportWidth;
			double yOffs = topMargin * wpfTextView.ViewportHeight;

			Size size;
			image.ClearValue(FrameworkElement.HeightProperty);
			image.ClearValue(FrameworkElement.WidthProperty);
			if (imageSourceService.Stretch == Stretch.None) {
				image.Stretch = Filter(imageSourceService.Stretch);
				image.StretchDirection = Filter(imageSourceService.StretchDirection);

				image.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
				size = image.DesiredSize;

				if (resizeTooBigImages && viewportWidth != 0 && viewportHeight != 0 && (size.Width > viewportWidth || size.Height > viewportHeight)) {
					image.Stretch = Stretch.Uniform;
					image.StretchDirection = StretchDirection.Both;
					image.Measure(new Size(viewportWidth, viewportHeight));
					size = image.DesiredSize;
					image.Width = size.Width;
					image.Height = size.Height;
				}
			}
			else {
				image.Measure(new Size(viewportWidth, viewportHeight));
				size = image.DesiredSize;
				image.Width = size.Width;
				image.Height = size.Height;
			}

			switch (imageSourceService.ImagePlacement) {
			case ImagePlacement.TopLeft:
				break;

			case ImagePlacement.TopRight:
				xOffs += viewportWidth - size.Width;
				break;

			case ImagePlacement.BottomLeft:
				yOffs += viewportHeight - size.Height;
				break;

			case ImagePlacement.BottomRight:
				xOffs += viewportWidth - size.Width;
				yOffs += viewportHeight - size.Height;
				break;

			case ImagePlacement.Top:
				xOffs += (viewportWidth - size.Width) / 2;
				break;

			case ImagePlacement.Left:
				yOffs += (viewportHeight - size.Height) / 2;
				break;

			case ImagePlacement.Right:
				xOffs += viewportWidth - size.Width;
				yOffs += (viewportHeight - size.Height) / 2;
				break;

			case ImagePlacement.Bottom:
				xOffs += (viewportWidth - size.Width) / 2;
				yOffs += viewportHeight - size.Height;
				break;

			case ImagePlacement.Center:
				xOffs += (viewportWidth - size.Width) / 2;
				yOffs += (viewportHeight - size.Height) / 2;
				break;

			default:
				Debug.Fail($"Unknown {nameof(ImagePlacement)} value: {imageSourceService.ImagePlacement}");
				break;
			}

			Canvas.SetLeft(image, wpfTextView.ViewportLeft + FilterOffset(imageSourceService.HorizontalOffset) + xOffs);
			Canvas.SetTop(image, wpfTextView.ViewportTop + FilterOffset(imageSourceService.VerticalOffset) + yOffs);
		}

		public void OnSettingsChanged() {
			if (currentImage == null) {
				currentImage = new Image();
				AddImageToAdornmentLayer();
			}
			InitializeImage(currentImage);
		}

		void AddImageToAdornmentLayer() {
			Debug.Assert(currentImage != null);
			adornmentLayer.AddAdornment(AdornmentPositioningBehavior.OwnerControlled, null, null, currentImage, null);
		}

		void WpfTextView_Closed(object sender, EventArgs e) {
			OnDisabled();
			wpfTextView.Closed -= WpfTextView_Closed;
			wpfTextView.LayoutChanged -= WpfTextView_LayoutChanged;
			imageSourceService.Unregister(this);
		}
	}
}
