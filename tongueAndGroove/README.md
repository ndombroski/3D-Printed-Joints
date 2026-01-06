# Tongue and Groove

Given a line as input, creates a tongue and groove the follows the given line for the given parts. 

It is recommended to use only straight line segments. Selecting an edge that consists of multiple consecutive line segments joined at angles is valid, but will not produce a smooth continuous surface. Using spline or bezier curves will work for the most part, however a non-zero fit clearance may not generate properly (this is because in order to add clearance, the faces at the ends of each tongue must be extruded to remove material).

For *most* applications, each input line should be on the surface of only a single tongue part. If one of the created tongue bodies abuts multiple tongue parts, it will be joined with the first part in the input list.
