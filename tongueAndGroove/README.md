# Tongue and Groove

Given a line as input, creates a tongue and groove the follows the given line for the given parts. 

It is recommended to either use a single line segment or a spline/bezier curve. Selecting an edge that consists of multiple consecutive line segments joined at angles is valid, but will not produce a smooth continuous surface.

For *most* applications, each input line should be on the surface of only a single tongue part. If one of the created tongue bodies abuts multiple tongue parts, it will be joined with the first part in the input list.
