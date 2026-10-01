Imports System.Windows
Imports System.Windows.Media
Imports System.Windows.Shapes

Public Class ProgressRingArc
    Inherits Shape

    Public Shared ReadOnly StartAngleProperty As DependencyProperty =
        DependencyProperty.Register(NameOf(StartAngle), GetType(Double), GetType(ProgressRingArc),
                                    New FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender))

    Public Shared ReadOnly EndAngleProperty As DependencyProperty =
        DependencyProperty.Register(NameOf(EndAngle), GetType(Double), GetType(ProgressRingArc),
                                    New FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender))

    Public Property StartAngle As Double
        Get
            Return CDbl(GetValue(StartAngleProperty))
        End Get
        Set(value As Double)
            SetValue(StartAngleProperty, value)
        End Set
    End Property

    Public Property EndAngle As Double
        Get
            Return CDbl(GetValue(EndAngleProperty))
        End Get
        Set(value As Double)
            SetValue(EndAngleProperty, value)
        End Set
    End Property

    Protected Overrides ReadOnly Property DefiningGeometry As Geometry
        Get
            Dim sweep = Math.Max(0.0, Math.Min(359.999, EndAngle - StartAngle))
            If sweep < 0.01 OrElse RenderSize.Width <= StrokeThickness OrElse RenderSize.Height <= StrokeThickness Then
                Return Geometry.Empty
            End If

            Dim radius = Math.Max(0.0, Math.Min(RenderSize.Width, RenderSize.Height) / 2.0 - StrokeThickness / 2.0)
            Dim center = New Point(RenderSize.Width / 2.0, RenderSize.Height / 2.0)
            Dim startPoint = PointOnCircle(center, radius, StartAngle - 90.0)
            Dim endPoint = PointOnCircle(center, radius, EndAngle - 90.0)
            Dim arcGeometry = New StreamGeometry()

            Using context = arcGeometry.Open()
                context.BeginFigure(startPoint, False, False)
                context.ArcTo(endPoint, New Size(radius, radius), 0.0, sweep > 180.0,
                              SweepDirection.Clockwise, True, False)
            End Using

            arcGeometry.Freeze()
            Return arcGeometry
        End Get
    End Property

    Private Shared Function PointOnCircle(center As Point, radius As Double, angle As Double) As Point
        Dim radians = angle * Math.PI / 180.0
        Return New Point(center.X + radius * Math.Cos(radians), center.Y + radius * Math.Sin(radians))
    End Function
End Class
