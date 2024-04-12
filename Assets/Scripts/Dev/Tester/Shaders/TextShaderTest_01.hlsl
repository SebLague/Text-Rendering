static const float NaN = -99999;
static const float infinity = 99999;

// Calculate roots of quadratic equation (value/s for which: a×t^2 + b×t + c = 0)
float2 CalculateQuadraticRoots(float a, float b, float c)
{
	float2 roots = NaN;

	// For a straight line, solve: b×t + c = 0; therefore t = -c/b
	if (abs(a) < 0.00001)
	{
		// Note: result is clamped between 0 and 1 because 
		if (b != 0) roots[0] = saturate(-c / b);
	}
	else
	{
		// Solve using quadratic formula: t = (-b ± sqrt(b^2 - 4ac)) / (2a)
		// If the value under the sqrt is negative, the equation has no real roots
		float discriminant = b * b - 4 * a * c;

		// Allow discriminant to be slightly negative to avoid a curve being missed due
		// to precision errors. Must be clamped to zero before it's used in sqrt though!
		if (discriminant > -1e-5)
		{
			float s = sqrt(max(0, discriminant));
			roots[0] = (-b + s) / (2 * a);
			roots[1] = (-b - s) / (2 * a);
		}
	}

	return roots;
}


bool isInside(float2 pixelPos, int dataOffset, out float3 debugCol)
{

	float window = 0;

	int pointOffset = GlyphMetaData[dataOffset];
	int numContours = GlyphMetaData[dataOffset + 1];
	dataOffset += 2;

	int numInt = 0;

	// Loop over all contours
	for (int contourIndex = 0; contourIndex < numContours; contourIndex++)
	{
		int signedContourLength = GlyphMetaData[dataOffset + contourIndex];
		int numPoints = abs(signedContourLength);

		for (int i = 0; i < numPoints; i += 2)
		{

			float2 p0 = BezierData[i + 0 + pointOffset] - pixelPos;
			float2 p1 = BezierData[i + 1 + pointOffset] - pixelPos;
			float2 p2 = BezierData[i + 2 + pointOffset] - pixelPos;

			// Skip curves that are entirely above or below the ray
			// (don't need to test p1 since curves are monotonic)
			bool isDownwardCurve = p0.y > p2.y;

			if (isDownwardCurve)
			{
				if (p0.y <= 0 && p2.y < 0) continue;
				if (p0.y >= 0 && p2.y > 0) continue;
			}
			else
			{
				if (p0.y < 0 && p2.y <= 0) continue;
				if (p0.y > 0 && p2.y >= 0) continue;
			}
			
			if (p0.y == p2.y) continue;

			float2 a = p0 - 2 * p1 + p2;
			float2 b = 2 * (p1 - p0);
			float2 c = p0;

			// Calculate roots and test if they're on the curve segment and to the right of the ray
			const float eps = 0.0001;
			float2 roots = CalculateQuadraticRoots(a.y, b.y, c.y);
			float t0 = saturate(roots[0]);
			float t1 = saturate(roots[1]);
			float intersect0 = a.x * t0 * t0 + b.x * t0 + c.x;
			float intersect1 = a.x * roots[1] * roots[1] + b.x * roots[1] + c.x;
			bool valid0 = (roots[0] >= -eps && roots[0] <= 1 + eps) && intersect0 > 0;
			bool valid1 = (roots[1] >= -eps && roots[1] <= 1 + eps) && intersect1 > 0;

			if (valid0 || valid1)
			{
				numInt += (isDownwardCurve ? 1 : -1);
			}

		}

		pointOffset += numPoints + 1;
	}

	bool insideGlyph = numInt > 0;
	debugCol = insideGlyph;
	return insideGlyph;
}
